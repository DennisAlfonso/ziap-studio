(() => {
    "use strict";

    const canvas = document.getElementById("stage");
    const status = document.getElementById("status");
    const errorPanel = document.getElementById("error");
    const emptyPanel = document.getElementById("empty");
    const toolbar = document.getElementById("toolbar");
    const attackInfo = document.getElementById("attack-info");
    const scheduler = document.getElementById("scheduler");
    const schedulerMode = document.getElementById("scheduler-mode");
    const schedulerPlay = document.getElementById("scheduler-play");
    const schedulerRange = document.getElementById("scheduler-range");
    const schedulerTime = document.getElementById("scheduler-time");
    const schedulerSequence = document.getElementById("scheduler-sequence");
    const schedulerContext = document.getElementById("scheduler-context");
    const schedulerRanges = document.getElementById("scheduler-ranges");
    const schedulerMarkers = document.getElementById("scheduler-markers");
    const schedulerRuntimeRanges = document.getElementById("scheduler-runtime-ranges");
    const schedulerRuntimeMarkers = document.getElementById("scheduler-runtime-markers");
    const schedulerTooltip = document.getElementById("scheduler-tooltip");
    const attackOnlyControls = Array.from(document.querySelectorAll(".attack-only"));
    const layerButtons = Array.from(document.querySelectorAll("[data-layer]"));
    const state = {
        app: null,
        scene: null,
        tilemap: null,
        overlay: null,
        layers: {},
        layerVisibility: {
            grid: true,
            regions: false,
            anchors: true,
            roles: true,
            attackTelegraph: true,
            attackHitbox: true,
            attackTrajectory: true,
            runtimeTrace: true,
        },
        attack: null,
        attackSequence: null,
        attackProbeByMap: {},
        temporalMode: false,
        schedulerFrame: 0,
        schedulerMaxFrame: 1,
        schedulerPlaying: false,
        schedulerAnimationFrame: 0,
        schedulerLastTimestamp: 0,
        schedulerLastPostedTimestamp: 0,
        cameraX: 0,
        cameraY: 0,
        zoom: 1,
        dragging: false,
        dragX: 0,
        dragY: 0,
        cameraStartX: 0,
        cameraStartY: 0,
        fitCameraToViewport: true,
        resizeObserver: null,
        resizeFrame: 0,
        sceneGeneration: 0,
    };

    function post(message) {
        if (window.chrome && window.chrome.webview) {
            window.chrome.webview.postMessage(message);
        }
    }

    function showError(message) {
        pauseScheduler();
        toolbar.hidden = true;
        emptyPanel.style.display = "none";
        attackInfo.style.display = "none";
        scheduler.hidden = true;
        errorPanel.style.display = "grid";
        errorPanel.textContent = `Arena Preview non disponibile\n${message}`;
        post({ type: "error", message: String(message) });
    }

    function projectUrl(relativePath) {
        const encoded = String(relativePath)
            .replace(/\\/g, "/")
            .split("/")
            .map(segment => encodeURIComponent(segment))
            .join("/");
        return `https://project.ziap/${encoded}`;
    }

    function ensureApp() {
        if (state.app) return;
        if (!window.PIXI || !window.Tilemap || !window.Bitmap) {
            throw new Error("PIXI o il Tilemap RPG Maker MZ non sono stati caricati.");
        }
        const viewport = getViewportSize();
        state.app = new PIXI.Application({
            view: canvas,
            width: viewport.width,
            height: viewport.height,
            backgroundColor: 0x101218,
            antialias: false,
            autoDensity: true,
            resolution: window.devicePixelRatio || 1,
        });
        Graphics._width = state.app.screen.width;
        Graphics._height = state.app.screen.height;
        state.app.ticker.add(() => {
            if (state.tilemap) state.tilemap.update();
        });
        state.resizeObserver = new ResizeObserver(queueResize);
        state.resizeObserver.observe(document.documentElement);
        window.addEventListener("resize", queueResize);
        queueResize();
    }

    function getViewportSize() {
        return {
            width: Math.max(1, Math.round(document.documentElement.clientWidth || window.innerWidth || 1)),
            height: Math.max(1, Math.round(document.documentElement.clientHeight || window.innerHeight || 1)),
        };
    }

    function queueResize() {
        if (state.resizeFrame) return;
        state.resizeFrame = requestAnimationFrame(() => {
            state.resizeFrame = 0;
            resize();
        });
    }

    function resize() {
        if (!state.app) return;
        const { width, height } = getViewportSize();
        state.app.renderer.resize(width, height);
        canvas.style.width = "100%";
        canvas.style.height = "100%";
        Graphics._width = state.app.screen.width;
        Graphics._height = state.app.screen.height;
        if (state.fitCameraToViewport) {
            resetCamera();
        } else {
            applyCamera();
        }
    }

    function clearScene() {
        if (!state.app) return;
        const children = state.app.stage.removeChildren();
        for (const child of children) child.destroy({ children: true });
        state.tilemap = null;
        state.overlay = null;
        state.layers = {};
    }

    async function loadScene(scene) {
        const generation = ++state.sceneGeneration;
        try {
            validateScene(scene);
            ensureApp();
            clearScene();
            state.scene = scene;
            errorPanel.style.display = "none";
            emptyPanel.style.display = "none";
            toolbar.hidden = false;

            const assets = [
                ...createParallax(scene),
                ...createTilemap(scene),
            ];
            createOverlay(scene);
            configureScheduler(false);
            resetCamera();
            status.textContent = `Caricamento di ${assets.length} asset della mappa…`;
            await Promise.all(assets.map(asset => waitForBitmap(asset.bitmap, asset.name)));
            if (generation !== state.sceneGeneration) return;
            applyCamera();
            post({ type: "sceneLoaded", mapId: scene.mapId });
        } catch (error) {
            if (generation !== state.sceneGeneration) return;
            showError(error && error.stack || error);
        }
    }

    function validateScene(scene) {
        if (!scene || !Number.isInteger(scene.width) || !Number.isInteger(scene.height) ||
            scene.width <= 0 || scene.height <= 0) {
            throw new Error("Dimensioni della mappa mancanti o non valide.");
        }
        const expectedValues = scene.width * scene.height * 6;
        if (!Array.isArray(scene.mapData) || scene.mapData.length < expectedValues) {
            throw new Error(`Dati tile incompleti: attesi ${expectedValues}, ricevuti ${scene.mapData?.length || 0}.`);
        }
        if (!Array.isArray(scene.tilesetNames) || scene.tilesetNames.length < 9) {
            throw new Error(`Definizione del tileset #${scene.tilesetId} mancante o incompleta.`);
        }
    }

    function showEmptyScene() {
        pauseScheduler();
        ++state.sceneGeneration;
        clearScene();
        state.scene = null;
        toolbar.hidden = true;
        errorPanel.style.display = "none";
        emptyPanel.style.display = "grid";
        attackInfo.style.display = "none";
        scheduler.hidden = true;
        status.textContent = "Nessuna mappa selezionata";
    }

    function createParallax(scene) {
        if (!scene.parallaxName) return [];
        const bitmap = Bitmap.load(projectUrl(`img/parallaxes/${scene.parallaxName}.png`));
        const parallax = new TilingSprite();
        parallax.bitmap = bitmap;
        parallax.move(0, 0, state.app.screen.width, state.app.screen.height);
        state.app.stage.addChild(parallax);
        state.layers.parallax = parallax;
        return [{ bitmap, name: `parallasse ${scene.parallaxName}` }];
    }

    function createTilemap(scene) {
        const tilemap = new Tilemap();
        tilemap.tileWidth = scene.tileWidth;
        tilemap.tileHeight = scene.tileHeight;
        tilemap.width = state.app.screen.width;
        tilemap.height = state.app.screen.height;
        tilemap.horizontalWrap = scene.scrollType === 2 || scene.scrollType === 3;
        tilemap.verticalWrap = scene.scrollType === 1 || scene.scrollType === 3;
        tilemap.setData(scene.width, scene.height, scene.mapData);
        tilemap.flags = scene.tilesetFlags;
        const assets = [];
        const bitmaps = scene.tilesetNames.map(name => {
            if (!name) return new Bitmap();
            const bitmap = Bitmap.load(projectUrl(`img/tilesets/${name}.png`));
            assets.push({ bitmap, name: `tileset ${name}` });
            return bitmap;
        });
        tilemap.setBitmaps(bitmaps);
        state.app.stage.addChild(tilemap);
        state.tilemap = tilemap;
        return assets;
    }

    function waitForBitmap(bitmap, name) {
        return new Promise((resolve, reject) => {
            const startedAt = performance.now();
            const inspect = () => {
                if (bitmap.isReady()) {
                    resolve();
                } else if (bitmap.isError()) {
                    reject(new Error(`Impossibile caricare ${name}.`));
                } else if (performance.now() - startedAt > 15000) {
                    reject(new Error(`Timeout durante il caricamento di ${name}.`));
                } else {
                    requestAnimationFrame(inspect);
                }
            };
            inspect();
        });
    }

    function createOverlay(scene) {
        const overlay = new PIXI.Container();
        state.app.stage.addChild(overlay);
        state.overlay = overlay;

        const regions = new PIXI.Graphics();
        drawRegions(regions, scene);
        regions.visible = state.layerVisibility.regions;
        overlay.addChild(regions);
        state.layers.regions = regions;

        const grid = new PIXI.Graphics();
        drawGrid(grid, scene);
        grid.visible = state.layerVisibility.grid;
        overlay.addChild(grid);
        state.layers.grid = grid;

        const anchors = new PIXI.Container();
        const roles = new PIXI.Container();
        for (const marker of scene.markers) {
            const target = marker.kind === 0 ? anchors : roles;
            target.addChild(createMarker(marker, scene));
        }
        overlay.addChild(anchors);
        overlay.addChild(roles);
        anchors.visible = state.layerVisibility.anchors;
        roles.visible = state.layerVisibility.roles;
        state.layers.anchors = anchors;
        state.layers.roles = roles;

        const bounds = new PIXI.Graphics();
        bounds.lineStyle(2, 0xffffff, 0.45);
        bounds.drawRect(0, 0, scene.width * scene.tileWidth, scene.height * scene.tileHeight);
        overlay.addChild(bounds);

        const attackTelegraph = new PIXI.Container();
        const attackHitbox = new PIXI.Container();
        const attackTrajectory = new PIXI.Container();
        const runtimeTrace = new PIXI.Container();
        attackTelegraph.visible = state.layerVisibility.attackTelegraph;
        attackHitbox.visible = state.layerVisibility.attackHitbox;
        attackTrajectory.visible = state.layerVisibility.attackTrajectory;
        runtimeTrace.visible = state.layerVisibility.runtimeTrace;
        overlay.addChild(attackTelegraph);
        overlay.addChild(attackHitbox);
        overlay.addChild(attackTrajectory);
        overlay.addChild(runtimeTrace);
        state.layers.attackTelegraph = attackTelegraph;
        state.layers.attackHitbox = attackHitbox;
        state.layers.attackTrajectory = attackTrajectory;
        state.layers.runtimeTrace = runtimeTrace;
        drawAttackGeometry();
        syncLayerButtons();
    }

    function drawGrid(graphics, scene) {
        const width = scene.width * scene.tileWidth;
        const height = scene.height * scene.tileHeight;
        graphics.lineStyle(1, 0xffffff, 0.12);
        for (let x = 0; x <= scene.width; x++) {
            graphics.moveTo(x * scene.tileWidth, 0);
            graphics.lineTo(x * scene.tileWidth, height);
        }
        for (let y = 0; y <= scene.height; y++) {
            graphics.moveTo(0, y * scene.tileHeight);
            graphics.lineTo(width, y * scene.tileHeight);
        }
    }

    function drawRegions(graphics, scene) {
        const offset = 5 * scene.width * scene.height;
        for (let y = 0; y < scene.height; y++) {
            for (let x = 0; x < scene.width; x++) {
                const regionId = scene.mapData[offset + y * scene.width + x] || 0;
                if (regionId <= 0) continue;
                graphics.beginFill(regionColor(regionId), 0.23);
                graphics.drawRect(
                    x * scene.tileWidth,
                    y * scene.tileHeight,
                    scene.tileWidth,
                    scene.tileHeight
                );
                graphics.endFill();
            }
        }
    }

    function regionColor(regionId) {
        const palette = [0x42a5f5, 0xab47bc, 0x26a69a, 0xffca28, 0xef5350, 0x7e57c2];
        return palette[regionId % palette.length];
    }

    function createMarker(marker, scene) {
        const container = new PIXI.Container();
        const isAnchor = marker.kind === 0;
        const color = isAnchor ? 0x51c8ff : 0xffca5c;
        const shape = new PIXI.Graphics();
        shape.lineStyle(2, color, 1);
        shape.beginFill(color, 0.24);
        if (isAnchor) {
            shape.drawCircle(0, 0, 10);
            shape.moveTo(-14, 0); shape.lineTo(14, 0);
            shape.moveTo(0, -14); shape.lineTo(0, 14);
        } else {
            shape.drawPolygon([0, -11, 11, 0, 0, 11, -11, 0]);
        }
        shape.endFill();
        container.addChild(shape);

        const label = new PIXI.Text(marker.id, {
            fontFamily: "Segoe UI",
            fontSize: 11,
            fill: color,
            stroke: 0x101218,
            strokeThickness: 3,
        });
        label.anchor.set(0.5, 1);
        label.y = -15;
        container.addChild(label);
        container.x = (marker.x + 0.5) * scene.tileWidth;
        container.y = (marker.y + 1) * scene.tileHeight;
        container.interactive = true;
        container.buttonMode = true;
        container.hitArea = new PIXI.Circle(0, 0, 18);
        container.on("pointertap", event => {
            event.stopPropagation();
            post({ type: "markerSelected", marker });
            status.textContent = `${isAnchor ? "Anchor" : "Ruolo"} ${marker.id} · evento #${marker.eventId} · (${marker.x}, ${marker.y})`;
        });
        return container;
    }

    function setAttackGeometry(attack, sequence) {
        const previousStepIndex = state.attack && state.attack.stepIndex;
        state.attack = attack || null;
        state.attackSequence = sequence || null;
        configureScheduler(previousStepIndex !== (state.attack && state.attack.stepIndex));
        if (Number.isFinite(sequence && sequence.previewFrame)) {
            state.schedulerFrame = Math.max(
                0,
                Math.min(sequence.previewFrame, state.schedulerMaxFrame)
            );
            updateSchedulerUi();
        }
        drawAttackGeometry();
    }

    function clearDisplayContainer(container) {
        if (!container) return;
        const children = container.removeChildren();
        for (const child of children) child.destroy({ children: true });
    }

    function drawAttackGeometry() {
        const telegraphLayer = state.layers.attackTelegraph;
        const hitboxLayer = state.layers.attackHitbox;
        const trajectoryLayer = state.layers.attackTrajectory;
        const runtimeLayer = state.layers.runtimeTrace;
        clearDisplayContainer(telegraphLayer);
        clearDisplayContainer(hitboxLayer);
        clearDisplayContainer(trajectoryLayer);
        clearDisplayContainer(runtimeLayer);
        const attack = state.attack;
        const scene = state.scene;
        const hasAttack = !!(attack && attack.geometry);
        const hasSequenceAttacks = !!(
            state.attackSequence && state.attackSequence.attacks.length
        );
        const hasTimelineSteps = !!(
            state.attackSequence && Array.isArray(state.attackSequence.steps) &&
            state.attackSequence.steps.length
        );
        const hasRuntime = !!(
            state.attackSequence && state.attackSequence.runtime &&
            Array.isArray(state.attackSequence.runtime.events) &&
            state.attackSequence.runtime.events.length
        );
        const canRender = !!(scene && telegraphLayer);
        const visible = canRender && (
            hasAttack || state.temporalMode && (hasSequenceAttacks || hasRuntime)
        );
        for (const control of attackOnlyControls) control.hidden = !canRender ||
            (!hasAttack && !hasSequenceAttacks && !hasRuntime);
        attackInfo.style.display = hasAttack && canRender ? "block" : "none";
        scheduler.hidden = !canRender || !hasTimelineSteps;
        if (scheduler.hidden) hideSchedulerTooltip();
        const schedulerOffset = scheduler.hidden ? "10px" : "126px";
        attackInfo.style.bottom = schedulerOffset;
        status.style.bottom = schedulerOffset;
        if (!visible) return;

        if (state.temporalMode && hasRuntime) {
            drawRuntimeTrace(scene);
        }

        if (!hasAttack && state.temporalMode && state.attackSequence) {
            drawScheduledAttacks(scene);
            syncLayerButtons();
            return;
        }

        const geometry = attack.geometry;
        const attackEntries = expandAttackEntries(attack);
        const primaryEntry = attackEntries[0];
        const target = primaryEntry.target;
        const probe = resolveAttackTarget(target, scene, primaryEntry);
        if (state.temporalMode && state.attackSequence) {
            drawScheduledAttacks(scene);
        } else {
            for (const entry of attackEntries) {
                const entryProbe = resolveAttackTarget(entry.target, scene, entry);
                if (geometry.kind === 1) {
                    drawProjectileGeometry(geometry, entry.target, entryProbe, scene, entry);
                } else {
                    drawInstantGeometry(geometry, entryProbe, scene);
                }
            }
        }
        updateAttackInfo(geometry, target, probe);
        syncLayerButtons();
    }

    function resolveAttackTarget(target, scene, attack) {
        if (Number.isFinite(target.targetX) && Number.isFinite(target.targetY)) {
            return { x: target.targetX, y: target.targetY };
        }
        const marker = findMarker(
            target.targetType === "anchor" ? 0 : target.targetType === "role" ? 1 : -1,
            target.targetAnchor || target.targetRole,
            scene,
            target.targetIndex
        );
        if (marker && !target.isDynamic) return { x: marker.x, y: marker.y };
        const probeKey = attackReferenceKey(
            target.targetType,
            target.targetKey,
            target.targetAnchor,
            target.targetRole,
            attack && attack.stepIndex,
            "target",
            attack && attack.targetOrdinal
        );
        const probes = state.attackProbeByMap[scene.mapId] ||
            (state.attackProbeByMap[scene.mapId] = {});
        const stored = probes[probeKey];
        if (stored) return stored;
        const probe = marker
            ? { x: marker.x, y: marker.y }
            : {
                x: Math.max(0, (scene.width - 1) / 2),
                y: Math.max(0, (scene.height - 1) / 2),
            };
        probes[probeKey] = probe;
        return probe;
    }

    function resolveAttackOrigin(target, probe, scene, attack) {
        if (Number.isFinite(target.originX) && Number.isFinite(target.originY)) {
            return { x: target.originX, y: target.originY };
        }
        if (target.originType === "captured") {
            const probes = state.attackProbeByMap[scene.mapId] ||
                (state.attackProbeByMap[scene.mapId] = {});
            const key = attackReferenceKey(
                "captured",
                target.originKey,
                "",
                "",
                attack && attack.stepIndex,
                "origin",
                attack && attack.targetOrdinal
            );
            if (probes[key]) return probes[key];
            const targetKey = attackReferenceKey(
                target.targetType,
                target.targetKey,
                target.targetAnchor,
                target.targetRole,
                attack && attack.stepIndex,
                "target",
                attack && attack.targetOrdinal
            );
            if (target.originKey && target.originKey === target.targetKey && probes[targetKey]) {
                probes[key] = probes[targetKey];
                return probes[key];
            }
            probes[key] = { x: probe.x, y: probe.y };
            return probes[key];
        }
        const marker = findMarker(
            target.originType === "anchor" ? 0 : 1,
            target.originAnchor || target.originRole || target.casterRole,
            scene
        );
        if (marker) return { x: marker.x, y: marker.y };
        return { x: Math.max(0, probe.x - 1), y: probe.y };
    }

    function attackReferenceKey(
        type,
        key,
        anchor,
        role,
        stepIndex,
        fallback,
        targetOrdinal = 0
    ) {
        const sequence = state.attackSequence && state.attackSequence.id || "sequence";
        if (type === "captured" && key) return `${sequence}:captured:${key}`;
        if (type === "anchor" && anchor) return `${sequence}:anchor:${anchor}`;
        if (type === "role" && role) return `${sequence}:role:${role}`;
        if (type === "playerPosition") return `${sequence}:step:${stepIndex}:${targetOrdinal}:${fallback}:player`;
        return `${sequence}:step:${stepIndex}:${targetOrdinal}:${fallback}`;
    }

    function findMarker(kind, id, scene, index = 0) {
        if (kind < 0 || !id) return null;
        const matches = scene.markers.filter(marker =>
            marker.kind === kind && String(marker.id).toLowerCase() === String(id).toLowerCase()
        );
        return matches[Math.max(0, Number(index) || 0)] || null;
    }

    function expandAttackEntries(entry) {
        const targets = Array.isArray(entry.targets) && entry.targets.length
            ? entry.targets
            : [entry.target || {}];
        return targets.map((target, targetOrdinal) => ({
            ...entry,
            target,
            targetOrdinal,
        }));
    }

    function mapPointToWorld(point, scene, offsetY = 0) {
        return {
            x: (point.x + 0.5) * scene.tileWidth,
            y: (point.y + 1 + offsetY) * scene.tileHeight,
        };
    }

    function drawInstantGeometry(geometry, probe, scene) {
        const telegraphCenter = mapPointToWorld(
            probe,
            scene,
            geometry.telegraphCenterOffsetYTiles || 0
        );
        const runtimeCenter = mapPointToWorld(
            probe,
            scene,
            geometry.runtimeCenterOffsetYTiles || 0
        );
        const radius = geometry.radiusTiles * scene.tileWidth;

        if (geometry.telegraphEnabled) {
            const telegraph = new PIXI.Graphics();
            telegraph.lineStyle(5, 0xff4f55, 0.88);
            telegraph.beginFill(0xff2028, 0.18);
            telegraph.drawCircle(telegraphCenter.x, telegraphCenter.y, radius);
            telegraph.endFill();
            state.layers.attackTelegraph.addChild(telegraph);
        }

        const hitbox = new PIXI.Graphics();
        hitbox.lineStyle(2, 0x53d9ff, 1);
        hitbox.drawCircle(runtimeCenter.x, runtimeCenter.y, radius);
        state.layers.attackHitbox.addChild(hitbox);
        drawExtraHurtboxes(geometry.extraHurtboxes || [], probe, scene, hitbox);

        const trajectory = new PIXI.Graphics();
        const rawPoint = mapPointToWorld(probe, scene);
        trajectory.lineStyle(2, 0xffcf5c, 0.95);
        trajectory.moveTo(rawPoint.x, rawPoint.y);
        trajectory.lineTo(runtimeCenter.x, runtimeCenter.y);
        drawCross(trajectory, rawPoint.x, rawPoint.y, 8, 0xffcf5c);
        state.layers.attackTrajectory.addChild(trajectory);
        addAttackLabel(
            state.layers.attackTrajectory,
            runtimeCenter.x,
            runtimeCenter.y - radius - 8,
            `Skill #${geometry.skillId} · r ${formatNumber(geometry.radiusTiles)} tile`
        );
    }

    function drawExtraHurtboxes(colliders, probe, scene, graphics) {
        for (const collider of colliders) {
            if (collider.kind === 0) {
                const center = mapPointToWorld(
                    {
                        x: probe.x + collider.offsetXTiles,
                        y: probe.y + collider.offsetYTiles,
                    },
                    scene,
                    -0.5
                );
                graphics.drawCircle(
                    center.x,
                    center.y,
                    collider.radiusTiles * scene.tileWidth
                );
            } else {
                const topLeft = mapPointToWorld({
                    x: probe.x - collider.widthTiles / 2 + collider.offsetXTiles,
                    y: probe.y - collider.heightTiles + collider.offsetYTiles,
                }, scene);
                graphics.drawRect(
                    topLeft.x,
                    topLeft.y,
                    collider.widthTiles * scene.tileWidth,
                    collider.heightTiles * scene.tileHeight
                );
            }
        }
    }

    function projectilePath(geometry, target, probe, scene, attack) {
        const origin = resolveAttackOrigin(target, probe, scene, attack);
        let endPoint = probe;
        if (geometry.directionMode !== 1) {
            let dx = probe.x - origin.x;
            let dy = probe.y - origin.y;
            if (geometry.directionMode === 0) {
                dx = Math.sign(dx);
                dy = Math.sign(dy);
            }
            const length = Math.sqrt(dx * dx + dy * dy) || 1;
            dx /= length;
            dy /= length;
            endPoint = {
                x: origin.x + dx * geometry.rangeTiles,
                y: origin.y + dy * geometry.rangeTiles,
            };
        }
        return {
            origin,
            endPoint,
            start: mapPointToWorld(origin, scene),
            end: mapPointToWorld(endPoint, scene),
        };
    }

    function drawProjectileGeometry(geometry, target, probe, scene, attack) {
        const path = projectilePath(geometry, target, probe, scene, attack);
        const { start, end } = path;
        const width = geometry.projectileColliderRadiusPixels * 2;
        if (geometry.telegraphEnabled) {
            state.layers.attackTelegraph.addChild(drawCapsule(
                start,
                end,
                width,
                0xff2028,
                0.18,
                0xff4f55,
                5
            ));
        }
        const hitbox = new PIXI.Graphics();
        for (const progress of [0, 0.5, 1]) {
            const point = interpolatePoint(start, end, progress);
            hitbox.lineStyle(2, 0x53d9ff, progress === 0.5 ? 1 : 0.45);
            hitbox.drawCircle(point.x, point.y, geometry.projectileColliderRadiusPixels);
        }
        state.layers.attackHitbox.addChild(hitbox);
        const trajectory = new PIXI.Graphics();
        trajectory.lineStyle(2, 0xffcf5c, 1);
        trajectory.moveTo(start.x, start.y);
        trajectory.lineTo(end.x, end.y);
        drawCross(trajectory, end.x, end.y, 8, 0xffcf5c);
        state.layers.attackTrajectory.addChild(trajectory);
        addAttackLabel(
            state.layers.attackTrajectory,
            (start.x + end.x) / 2,
            (start.y + end.y) / 2 - width / 2 - 8,
            `Skill #${geometry.skillId} · ${formatNumber(geometry.rangeTiles)} tile`
        );
    }

    function interpolatePoint(start, end, progress) {
        return {
            x: start.x + (end.x - start.x) * progress,
            y: start.y + (end.y - start.y) * progress,
        };
    }

    function drawCapsule(start, end, width, fill, fillAlpha, line, lineWidth) {
        const dx = end.x - start.x;
        const dy = end.y - start.y;
        const length = Math.max(1, Math.sqrt(dx * dx + dy * dy));
        const graphics = new PIXI.Graphics();
        graphics.position.set(start.x, start.y);
        graphics.rotation = Math.atan2(dy, dx);
        graphics.lineStyle(lineWidth, line, 1);
        if (fillAlpha > 0) graphics.beginFill(fill, fillAlpha);
        graphics.drawRoundedRect(0, -width / 2, length, width, width / 2);
        if (fillAlpha > 0) graphics.endFill();
        return graphics;
    }

    function drawCross(graphics, x, y, size, color) {
        graphics.lineStyle(2, color, 1);
        graphics.moveTo(x - size, y); graphics.lineTo(x + size, y);
        graphics.moveTo(x, y - size); graphics.lineTo(x, y + size);
    }

    function configureScheduler(selectionChanged) {
        const sequence = state.attackSequence;
        const attacks = sequence && Array.isArray(sequence.attacks) ? sequence.attacks : [];
        const runtimeEvents = sequence && sequence.runtime &&
            Array.isArray(sequence.runtime.events) ? sequence.runtime.events : [];
        let maximum = Math.max(1, Number(sequence && sequence.minimumDurationFrames) || 1);
        if (state.scene) {
            for (const entry of attacks) {
                for (const expanded of expandAttackEntries(entry)) {
                    const schedule = buildAttackSchedule(expanded, state.scene);
                    maximum = Math.max(maximum, schedule.endFrame);
                }
            }
        }
        for (const event of runtimeEvents) {
            maximum = Math.max(
                maximum,
                (Number(event.frame) || 0) + (Number(event.durationFrames) || 0)
            );
        }
        state.schedulerMaxFrame = Math.ceil(maximum);
        schedulerRange.max = String(state.schedulerMaxFrame);
        if (selectionChanged && state.attack) {
            const selected = attacks.find(entry => entry.stepIndex === state.attack.stepIndex);
            state.schedulerFrame = selected ? selected.startFrame : 0;
        }
        state.schedulerFrame = Math.max(0, Math.min(state.schedulerFrame, state.schedulerMaxFrame));
        renderSchedulerTimeline();
        updateSchedulerUi();
    }

    function schedulerPercent(frame) {
        return Math.max(0, Math.min(100,
            (Number(frame) || 0) / Math.max(1, state.schedulerMaxFrame) * 100));
    }

    function schedulerStepClass(step) {
        const kind = String(step.kind || "").toLowerCase();
        const label = String(step.label || "").toLowerCase();
        if (step.attack) return "attack";
        if (kind === "waituntil") return "sync";
        if (kind === "sequence" || kind === "repeatsequence") return "sequence";
        if (label.includes("capture")) return "capture";
        if (label.includes("cue")) return "cue";
        return "action";
    }

    function schedulerStepIcon(step) {
        const category = schedulerStepClass(step);
        if (category === "attack") return "⚡";
        if (category === "sync") return "⌛";
        if (category === "sequence") return String(step.kind).toLowerCase() === "repeatsequence" ? "↻" : "↳";
        if (category === "capture") return "◎";
        if (category === "cue") return "✦";
        if (String(step.label || "").toLowerCase().includes("move")) return "➜";
        return "◆";
    }

    function appendSchedulerRange(startFrame, endFrame, className, container = schedulerRanges) {
        const start = schedulerPercent(startFrame);
        const end = schedulerPercent(endFrame);
        const segment = document.createElement("span");
        segment.className = `scheduler-range-segment ${className || ""}`.trim();
        segment.style.left = `${start}%`;
        segment.style.width = `${Math.max(.25, end - start)}%`;
        container.appendChild(segment);
    }

    function renderSchedulerTimeline() {
        schedulerRanges.replaceChildren();
        schedulerMarkers.replaceChildren();
        schedulerRuntimeRanges.replaceChildren();
        schedulerRuntimeMarkers.replaceChildren();
        const sequence = state.attackSequence;
        const steps = sequence && Array.isArray(sequence.steps) ? sequence.steps : [];
        const runtime = sequence && sequence.runtime;
        const runtimeEvents = runtime && Array.isArray(runtime.events) ? runtime.events : [];
        const comparisons = runtime && Array.isArray(runtime.comparisons) ? runtime.comparisons : [];
        schedulerSequence.textContent = sequence && sequence.id ? sequence.id : "Sequenza";
        schedulerContext.textContent = runtime && runtime.traceName
            ? `${runtime.traceName} · click sugli eventi runtime`
            : "nessun trace runtime · usa il playtest per registrarlo";
        for (const step of steps) {
            const duration = Number(step.durationFrames);
            if (String(step.kind).toLowerCase() === "wait" && Number.isFinite(duration) && duration > 0) {
                appendSchedulerRange(step.frame, Number(step.frame) + duration, "wait");
            }
            if (step.attack) {
                const attackEnd = Number(step.frame) + Math.max(
                    Number(step.attack.telegraphDurationFrames) || 0,
                    Number(step.attack.executionDelayFrames) || 0
                );
                appendSchedulerRange(step.frame, attackEnd, "attack");
            }
        }
        const groups = new Map();
        for (const step of steps.filter(entry => String(entry.kind).toLowerCase() !== "wait")) {
            const frame = Number(step.frame) || 0;
            if (!groups.has(frame)) groups.set(frame, []);
            groups.get(frame).push(step);
        }
        for (const [frame, group] of [...groups.entries()].sort((left, right) => left[0] - right[0])) {
            const marker = document.createElement("button");
            marker.type = "button";
            marker.className = `scheduler-marker ${schedulerStepClass(group[0])}`;
            marker.style.left = `${schedulerPercent(frame)}%`;
            marker.textContent = schedulerStepIcon(group[0]);
            marker.dataset.stepIndexes = group.map(step => step.stepIndex).join(",");
            marker.setAttribute("aria-label", schedulerGroupTitle(frame, group));
            if (group.length > 1) {
                const count = document.createElement("span");
                count.className = "scheduler-marker-count";
                count.textContent = String(group.length);
                marker.appendChild(count);
            }
            marker.addEventListener("pointerenter", () => showSchedulerTooltip(marker, frame, group));
            marker.addEventListener("pointerleave", () => {
                if (document.activeElement !== marker) hideSchedulerTooltip();
            });
            marker.addEventListener("focus", () => showSchedulerTooltip(marker, frame, group));
            marker.addEventListener("blur", hideSchedulerTooltip);
            marker.addEventListener("click", event => {
                event.stopPropagation();
                selectSchedulerGroupStep(frame, group);
            });
            schedulerMarkers.appendChild(marker);
        }
        renderRuntimeScheduler(runtimeEvents, comparisons);
        updateSchedulerMarkerSelection();
    }

    function renderRuntimeScheduler(events, comparisons) {
        for (const event of events.filter(entry => entry.type === "telegraph.started")) {
            const endFrame = Number(event.frame) + Math.max(1, Number(event.durationFrames) || 1);
            appendSchedulerRange(event.frame, endFrame, "runtime", schedulerRuntimeRanges);
        }
        for (const comparison of comparisons) {
            if (!Number.isFinite(Number(comparison.runtimeExecutionFrame))) continue;
            const start = Math.min(
                Number(comparison.expectedExecutionFrame) || 0,
                Number(comparison.runtimeExecutionFrame) || 0
            );
            const end = Math.max(
                Number(comparison.expectedExecutionFrame) || 0,
                Number(comparison.runtimeExecutionFrame) || 0
            );
            appendSchedulerRange(
                start,
                Math.max(start + .1, end),
                `runtime ${String(comparison.status || "").toLowerCase()}`,
                schedulerRuntimeRanges
            );
        }
        const relevantTypes = new Set([
            "sequence.step.started",
            "telegraph.started",
            "cast.executed",
            "collider.activated",
            "projectile.hit",
        ]);
        const groups = new Map();
        for (const event of events.filter(entry => relevantTypes.has(entry.type))) {
            const frame = Number(event.frame) || 0;
            if (!groups.has(frame)) groups.set(frame, []);
            groups.get(frame).push(event);
        }
        for (const [frame, group] of [...groups.entries()].sort((left, right) => left[0] - right[0])) {
            const marker = document.createElement("button");
            const comparison = runtimeComparisonForEvent(group[0]);
            const fidelity = String(comparison && comparison.status || "").toLowerCase();
            marker.type = "button";
            marker.className = `scheduler-marker runtime ${fidelity}`.trim();
            marker.style.left = `${schedulerPercent(frame)}%`;
            marker.textContent = runtimeEventIcon(group[0]);
            marker.dataset.stepIndexes = group
                .map(event => event.stepIndex)
                .filter(Number.isFinite)
                .join(",");
            marker.setAttribute("aria-label", runtimeGroupTitle(frame, group));
            if (group.length > 1) {
                const count = document.createElement("span");
                count.className = "scheduler-marker-count";
                count.textContent = String(group.length);
                marker.appendChild(count);
            }
            marker.addEventListener("pointerenter", () => showRuntimeTooltip(marker, frame, group));
            marker.addEventListener("pointerleave", () => {
                if (document.activeElement !== marker) hideSchedulerTooltip();
            });
            marker.addEventListener("focus", () => showRuntimeTooltip(marker, frame, group));
            marker.addEventListener("blur", hideSchedulerTooltip);
            marker.addEventListener("click", event => {
                event.stopPropagation();
                selectRuntimeGroup(frame, group);
            });
            schedulerRuntimeMarkers.appendChild(marker);
        }
    }

    function runtimeComparisonForEvent(event) {
        const comparisons = state.attackSequence && state.attackSequence.runtime &&
            state.attackSequence.runtime.comparisons || [];
        if (event.castId) {
            const byCast = comparisons.find(item => item.castId === event.castId);
            if (byCast) return byCast;
        }
        return comparisons.find(item =>
            Number(item.stepIndex) === Number(event.stepIndex) &&
            (!Number.isFinite(Number(event.skillId)) || Number(item.skillId) === Number(event.skillId)) &&
            (!Number.isFinite(Number(event.castIndex)) || Number(item.castIndex) === Number(event.castIndex))
        ) || null;
    }

    function runtimeEventIcon(event) {
        if (event.type === "telegraph.started") return "◌";
        if (event.type === "cast.executed") return "⚡";
        if (event.type === "collider.activated") return "◎";
        if (event.type === "projectile.hit") return "×";
        if (String(event.action || "").toLowerCase().includes("move")) return "➜";
        return "▶";
    }

    function runtimeEventLabel(event) {
        const labels = {
            "sequence.step.started": event.action || "step avviato",
            "telegraph.started": "telegraph mostrato",
            "cast.executed": "skill eseguita",
            "collider.activated": "collider attivo",
            "projectile.hit": "proiettile a segno",
        };
        return labels[event.type] || event.type;
    }

    function runtimeGroupTitle(frame, group) {
        return group.length === 1
            ? `Runtime F ${formatNumber(frame)} · ${runtimeEventLabel(group[0])}`
            : `Runtime F ${formatNumber(frame)} · ${group.length} eventi`;
    }

    function runtimeEventTooltip(event) {
        const lines = [`${runtimeEventIcon(event)} F ${formatNumber(event.frame)} · ${runtimeEventLabel(event)}`];
        if (Number.isFinite(Number(event.skillId))) {
            lines.push(`   Skill #${event.skillId} · cast ${Number(event.castIndex) + 1}`);
        }
        if (Number.isFinite(Number(event.targetCount))) {
            lines.push(`   ${event.targetCount} target effettivi`);
        }
        const comparison = runtimeComparisonForEvent(event);
        if (comparison) {
            lines.push(`   ${comparison.status}: ${comparison.summary}`);
        }
        return lines.join("\n");
    }

    function showRuntimeTooltip(anchor, frame, group) {
        schedulerTooltip.replaceChildren();
        const title = document.createElement("strong");
        title.textContent = runtimeGroupTitle(frame, group);
        const content = document.createElement("span");
        const details = group.map(runtimeEventTooltip);
        content.textContent = group.length === 1
            ? details[0].split("\n").slice(1).join("\n")
            : details.join("\n\n");
        schedulerTooltip.append(title, content);
        positionSchedulerTooltip(anchor);
    }

    function selectRuntimeGroup(frame, group) {
        pauseScheduler();
        state.temporalMode = true;
        state.schedulerFrame = Math.max(0, Math.min(Number(frame) || 0, state.schedulerMaxFrame));
        const correlated = group.find(event => Number.isFinite(Number(event.stepIndex)));
        if (correlated && state.attackSequence) {
            state.attackSequence.selectedStepIndex = Number(correlated.stepIndex);
        }
        updateSchedulerUi();
        drawAttackGeometry();
        if (correlated) {
            post({
                type: "timelineStepSelected",
                stepIndex: Number(correlated.stepIndex),
                frame: state.schedulerFrame,
            });
        } else {
            postSchedulerFrame(true);
        }
    }

    function schedulerGroupTitle(frame, group) {
        const prefix = group.every(step => step.isStartExact !== false) ? "F" : "≥ F";
        return group.length === 1
            ? `${prefix} ${formatNumber(frame)} · ${group[0].label}`
            : `${prefix} ${formatNumber(frame)} · ${group.length} eventi`;
    }

    function schedulerStepTooltip(step) {
        const prefix = step.isStartExact === false ? "≥ F" : "F";
        const lines = [`${schedulerStepIcon(step)} ${prefix} ${formatNumber(step.frame)} · ${step.label}`];
        if (step.detail) lines.push(`   ${step.detail}`);
        if (step.attack) {
            const executionFrame = Number(step.frame) + (Number(step.attack.executionDelayFrames) || 0);
            lines.push(
                `   Skill #${step.attack.skillId} ${step.attack.skillName || ""} · telegraph ${step.attack.telegraphDurationFrames}f · impatto F ${formatNumber(executionFrame)}`,
                `   ${step.attack.targetCount} target · avvii ×${step.attack.repeatOnUseCount} · hit ×${step.attack.hitRepeatCount}`
            );
        } else if (Number.isFinite(Number(step.durationFrames)) && Number(step.durationFrames) > 0) {
            lines.push(`   durata ${step.durationFrames}f · fine F ${Number(step.frame) + Number(step.durationFrames)}`);
        } else if (Number.isFinite(Number(step.timeoutFrames)) && Number(step.timeoutFrames) > 0) {
            lines.push(`   timeout ${step.timeoutFrames}f · durata dipendente dal runtime`);
        }
        return lines.join("\n");
    }

    function showSchedulerTooltip(anchor, frame, group) {
        schedulerTooltip.replaceChildren();
        const title = document.createElement("strong");
        title.textContent = schedulerGroupTitle(frame, group);
        const content = document.createElement("span");
        const details = group.map(schedulerStepTooltip);
        content.textContent = group.length === 1
            ? details[0].split("\n").slice(1).join("\n")
            : details.join("\n\n");
        schedulerTooltip.append(title, content);
        positionSchedulerTooltip(anchor);
    }

    function positionSchedulerTooltip(anchor) {
        schedulerTooltip.style.display = "block";
        const anchorRect = anchor.getBoundingClientRect();
        const tooltipRect = schedulerTooltip.getBoundingClientRect();
        const left = Math.max(12, Math.min(
            window.innerWidth - tooltipRect.width - 12,
            anchorRect.left + anchorRect.width / 2 - tooltipRect.width / 2
        ));
        const top = Math.max(12, anchorRect.top - tooltipRect.height - 8);
        schedulerTooltip.style.left = `${left}px`;
        schedulerTooltip.style.top = `${top}px`;
    }

    function hideSchedulerTooltip() {
        schedulerTooltip.style.display = "none";
    }

    function selectSchedulerGroupStep(frame, group) {
        pauseScheduler();
        state.temporalMode = true;
        const currentIndex = group.findIndex(step =>
            step.stepIndex === (state.attackSequence && state.attackSequence.selectedStepIndex));
        const selected = group[(currentIndex + 1) % group.length];
        if (state.attackSequence) state.attackSequence.selectedStepIndex = selected.stepIndex;
        state.schedulerFrame = Math.max(0, Math.min(Number(frame) || 0, state.schedulerMaxFrame));
        updateSchedulerUi();
        drawAttackGeometry();
        post({
            type: "timelineStepSelected",
            stepIndex: selected.stepIndex,
            frame: state.schedulerFrame,
        });
    }

    function updateSchedulerMarkerSelection() {
        const selectedStepIndex = state.attackSequence && state.attackSequence.selectedStepIndex;
        for (const marker of [
            ...schedulerMarkers.querySelectorAll(".scheduler-marker"),
            ...schedulerRuntimeMarkers.querySelectorAll(".scheduler-marker"),
        ]) {
            const indexes = String(marker.dataset.stepIndexes || "")
                .split(",")
                .map(Number);
            marker.classList.toggle("selected", indexes.includes(selectedStepIndex));
        }
    }

    function buildAttackSchedule(entry, scene) {
        const geometry = entry.geometry;
        const target = entry.target || {};
        const probe = resolveAttackTarget(target, scene, entry);
        const startFrame = Number(entry.startFrame) || 0;
        const executionFrame = startFrame + (Number(geometry.executionDelayFrames) || 0);
        const repeatDelayFrames = Math.max(
            0,
            (Number(geometry.repeatDelayMilliseconds) || 0) * 60 / 1000
        );
        const useCount = Math.max(1, Number(geometry.repeatOnUseCount) || 1);
        const hitCount = Math.max(1, Number(geometry.hitRepeatCount) || 1);
        const launches = Array.from(
            { length: useCount },
            (_, index) => executionFrame + repeatDelayFrames * index
        );
        let path = null;
        let travelFrames = 1;
        if (geometry.kind === 1) {
            path = projectilePath(geometry, target, probe, scene, entry);
            const distancePixels = Math.hypot(
                path.end.x - path.start.x,
                path.end.y - path.start.y
            );
            travelFrames = Math.max(1, distancePixels / Math.max(0.01, geometry.speed));
        }
        const lastLaunch = launches[launches.length - 1] || executionFrame;
        const lastHitOffset = geometry.kind === 1
            ? 0
            : repeatDelayFrames * Math.max(0, hitCount - 1);
        return {
            entry,
            geometry,
            target,
            probe,
            path,
            startFrame,
            executionFrame,
            telegraphEndFrame: startFrame + (Number(geometry.telegraphDurationFrames) || 0),
            repeatDelayFrames,
            hitCount,
            launches,
            travelFrames,
            endFrame: lastLaunch + travelFrames + lastHitOffset,
        };
    }

    function drawRuntimeTrace(scene) {
        const layer = state.layers.runtimeTrace;
        const runtime = state.attackSequence && state.attackSequence.runtime;
        const events = runtime && Array.isArray(runtime.events) ? runtime.events : [];
        if (!layer || !events.length) return;
        const frame = state.schedulerFrame;

        drawRuntimeActorTrail(layer, events, "boss", frame, scene, 0xff7bd8);
        drawRuntimeActorTrail(layer, events, "player", frame, scene, 0x66e3ff);
        drawRuntimeActor(layer, interpolatedRuntimeActor(events, "boss", frame), "BOSS", scene, 0xff7bd8);
        drawRuntimeActor(layer, interpolatedRuntimeActor(events, "player", frame), "PLAYER", scene, 0x66e3ff);

        for (const event of events.filter(entry => entry.type === "telegraph.started")) {
            const start = Number(event.frame) || 0;
            const end = start + Math.max(1, Number(event.durationFrames) || 1);
            if (frame < start || frame > end) continue;
            drawRuntimeTelegraph(layer, event, scene);
        }
        for (const event of events.filter(entry =>
            entry.type === "collider.activated" && Math.abs(frame - Number(entry.frame)) <= 1.25)) {
            drawRuntimeCollider(layer, event, scene);
        }
        for (const event of events.filter(entry =>
            entry.type === "projectile.hit" && Math.abs(frame - Number(entry.frame)) <= 2.25)) {
            const point = event.target || event.point;
            if (!validRuntimePoint(point)) continue;
            const world = mapPointToWorld(point, scene);
            const hit = new PIXI.Graphics();
            drawCross(hit, world.x, world.y, 12, 0xff73a8);
            layer.addChild(hit);
        }
        drawRuntimeProjectiles(layer, events, frame, scene);
    }

    function validRuntimePoint(point) {
        return !!(point && Number.isFinite(Number(point.x)) && Number.isFinite(Number(point.y)));
    }

    function interpolatedRuntimeActor(events, property, frame) {
        const samples = events.filter(event =>
            event.type === "actors.sample" && validRuntimePoint(event[property]));
        let before = null;
        let after = null;
        for (const sample of samples) {
            if (Number(sample.frame) <= frame) before = sample;
            if (Number(sample.frame) >= frame) {
                after = sample;
                break;
            }
        }
        const first = before || after;
        const second = after || before;
        if (!first || !second) return null;
        const firstPoint = first[property];
        const secondPoint = second[property];
        const span = Number(second.frame) - Number(first.frame);
        if (span <= 0) return firstPoint;
        return {
            ...firstPoint,
            ...interpolatePoint(firstPoint, secondPoint, Math.max(0, Math.min(1,
                (frame - Number(first.frame)) / span
            ))),
        };
    }

    function drawRuntimeActorTrail(layer, events, property, frame, scene, color) {
        const samples = events.filter(event =>
            event.type === "actors.sample" &&
            Number(event.frame) >= frame - 90 &&
            Number(event.frame) <= frame &&
            validRuntimePoint(event[property]));
        if (samples.length < 2) return;
        const trail = new PIXI.Graphics();
        trail.lineStyle(2, color, .28);
        samples.forEach((sample, index) => {
            const point = mapPointToWorld(sample[property], scene);
            if (index === 0) trail.moveTo(point.x, point.y);
            else trail.lineTo(point.x, point.y);
        });
        layer.addChild(trail);
    }

    function drawRuntimeActor(layer, point, label, scene, color) {
        if (!validRuntimePoint(point)) return;
        const world = mapPointToWorld(point, scene);
        const actor = new PIXI.Graphics();
        actor.lineStyle(point.jumping ? 4 : 3, color, .95);
        actor.beginFill(color, .16);
        actor.drawCircle(world.x, world.y, point.jumping ? 15 : 11);
        actor.endFill();
        drawCross(actor, world.x, world.y, 5, color);
        layer.addChild(actor);
        addAttackLabel(
            layer,
            world.x,
            world.y - 17,
            `${label} runtime${point.jumping ? " · salto" : ""}`
        );
    }

    function drawRuntimeTelegraph(layer, event, scene) {
        const color = 0xd87cff;
        if (String(event.kind).toLowerCase().includes("projectile") &&
            validRuntimePoint(event.origin) && validRuntimePoint(event.end)) {
            const origin = mapPointToWorld(event.origin, scene);
            const end = mapPointToWorld(event.end, scene);
            layer.addChild(drawCapsule(
                origin,
                end,
                Math.max(2, Number(event.corridorWidthPixels) || 16),
                color,
                .09,
                color,
                2
            ));
            return;
        }
        const point = event.center || event.point;
        if (!validRuntimePoint(point)) return;
        const world = mapPointToWorld(point, scene);
        const telegraph = new PIXI.Graphics();
        telegraph.lineStyle(2, color, .95);
        telegraph.beginFill(color, .08);
        telegraph.drawCircle(
            world.x,
            world.y,
            Math.max(4, Number(event.radiusTiles) * scene.tileWidth || 8)
        );
        telegraph.endFill();
        layer.addChild(telegraph);
    }

    function drawRuntimeCollider(layer, event, scene) {
        const point = event.center || event.point;
        if (!validRuntimePoint(point)) return;
        const comparison = runtimeComparisonForEvent(event);
        const statusName = String(comparison && comparison.status || "").toLowerCase();
        const color = statusName === "aligned" ? 0x50d297 :
            statusName === "drift" ? 0xffbe52 :
                statusName ? 0xff5b61 : 0xd87cff;
        const geometry = event.geometry || {};
        const radius = Number(geometry.radiusTiles) > 0
            ? Number(geometry.radiusTiles) * scene.tileWidth
            : Math.max(4, Number(geometry.colliderRadiusPixels) ||
                Number(event.colliderRadiusPixels) || 8);
        const world = mapPointToWorld(point, scene);
        const collider = new PIXI.Graphics();
        collider.lineStyle(4, color, .95);
        collider.beginFill(color, .18);
        collider.drawCircle(world.x, world.y, radius);
        collider.endFill();
        layer.addChild(collider);
        addAttackLabel(
            layer,
            world.x,
            world.y - radius - 5,
            `Runtime Skill #${event.skillId || "?"}${comparison ? ` · ${comparison.status}` : ""}`
        );
    }

    function drawRuntimeProjectiles(layer, events, frame, scene) {
        const samplesByProjectile = new Map();
        for (const event of events.filter(entry =>
            entry.type === "projectile.sample" && entry.projectileId && validRuntimePoint(entry.point))) {
            if (!samplesByProjectile.has(event.projectileId)) samplesByProjectile.set(event.projectileId, []);
            samplesByProjectile.get(event.projectileId).push(event);
        }
        for (const samples of samplesByProjectile.values()) {
            const nearest = samples.reduce((current, sample) =>
                !current || Math.abs(Number(sample.frame) - frame) <
                    Math.abs(Number(current.frame) - frame) ? sample : current, null);
            if (!nearest || Math.abs(Number(nearest.frame) - frame) > 2) continue;
            const visibleTrail = samples.filter(sample =>
                Number(sample.frame) <= frame && Number(sample.frame) >= frame - 45);
            if (visibleTrail.length > 1) {
                const trail = new PIXI.Graphics();
                trail.lineStyle(2, 0xd87cff, .58);
                visibleTrail.forEach((sample, index) => {
                    const point = mapPointToWorld(sample.point, scene);
                    if (index === 0) trail.moveTo(point.x, point.y);
                    else trail.lineTo(point.x, point.y);
                });
                layer.addChild(trail);
            }
            const world = mapPointToWorld(nearest.point, scene);
            const projectile = new PIXI.Graphics();
            projectile.lineStyle(3, 0xd87cff, 1);
            projectile.beginFill(0xd87cff, .25);
            projectile.drawCircle(
                world.x,
                world.y,
                Math.max(5, Number(nearest.colliderRadiusPixels) || 8)
            );
            projectile.endFill();
            layer.addChild(projectile);
        }
    }

    function drawScheduledAttacks(scene) {
        const entries = state.attackSequence && state.attackSequence.attacks || [];
        const frame = state.schedulerFrame;
        for (const entry of entries) {
            for (const expanded of expandAttackEntries(entry)) {
                const schedule = buildAttackSchedule(expanded, scene);
                const selected = entry.stepIndex === state.attackSequence.selectedStepIndex;
                if (schedule.geometry.kind === 1) {
                    drawScheduledProjectile(schedule, frame, selected);
                } else {
                    drawScheduledInstant(schedule, frame, selected, scene);
                }
            }
        }
    }

    function drawScheduledProjectile(schedule, frame, selected) {
        const { geometry, path } = schedule;
        const width = geometry.projectileColliderRadiusPixels * 2;
        if (geometry.telegraphEnabled &&
            frame >= schedule.startFrame && frame <= schedule.telegraphEndFrame) {
            state.layers.attackTelegraph.addChild(drawCapsule(
                path.start,
                path.end,
                width,
                0xff2028,
                0.18,
                0xff4f55,
                selected ? 5 : 3
            ));
        }

        const trajectory = new PIXI.Graphics();
        trajectory.lineStyle(selected ? 2 : 1, 0xffcf5c, selected ? 1 : 0.35);
        trajectory.moveTo(path.start.x, path.start.y);
        trajectory.lineTo(path.end.x, path.end.y);
        state.layers.attackTrajectory.addChild(trajectory);

        const hitbox = new PIXI.Graphics();
        let activeCount = 0;
        for (const launchFrame of schedule.launches) {
            const age = frame - launchFrame;
            if (age < 0 || age > schedule.travelFrames) continue;
            const position = interpolatePoint(
                path.start,
                path.end,
                Math.max(0, Math.min(1, age / schedule.travelFrames))
            );
            hitbox.lineStyle(2, 0x53d9ff, 1);
            hitbox.beginFill(0x53d9ff, 0.16);
            hitbox.drawCircle(position.x, position.y, geometry.projectileColliderRadiusPixels);
            hitbox.endFill();
            activeCount++;
        }
        state.layers.attackHitbox.addChild(hitbox);
        if (selected) {
            addAttackLabel(
                state.layers.attackTrajectory,
                (path.start.x + path.end.x) / 2,
                (path.start.y + path.end.y) / 2 - width / 2 - 8,
                `Skill #${geometry.skillId} · ${activeCount}/${schedule.launches.length} proiettili attivi`
            );
        }
    }

    function drawScheduledInstant(schedule, frame, selected, scene) {
        const geometry = schedule.geometry;
        const telegraphCenter = mapPointToWorld(
            schedule.probe,
            scene,
            geometry.telegraphCenterOffsetYTiles || 0
        );
        const runtimeCenter = mapPointToWorld(
            schedule.probe,
            scene,
            geometry.runtimeCenterOffsetYTiles || 0
        );
        const radius = geometry.radiusTiles * scene.tileWidth;
        if (geometry.telegraphEnabled &&
            frame >= schedule.startFrame && frame <= schedule.telegraphEndFrame) {
            const telegraph = new PIXI.Graphics();
            telegraph.lineStyle(selected ? 5 : 3, 0xff4f55, selected ? 0.88 : 0.55);
            telegraph.beginFill(0xff2028, selected ? 0.18 : 0.09);
            telegraph.drawCircle(telegraphCenter.x, telegraphCenter.y, radius);
            telegraph.endFill();
            state.layers.attackTelegraph.addChild(telegraph);
        }

        const hitbox = new PIXI.Graphics();
        for (const launchFrame of schedule.launches) {
            for (let hit = 0; hit < schedule.hitCount; hit++) {
                const hitFrame = launchFrame + schedule.repeatDelayFrames * hit;
                if (Math.abs(frame - hitFrame) > 0.75) continue;
                hitbox.lineStyle(2, 0x53d9ff, 1);
                hitbox.beginFill(0x53d9ff, 0.16);
                hitbox.drawCircle(runtimeCenter.x, runtimeCenter.y, radius);
                hitbox.endFill();
                drawExtraHurtboxes(geometry.extraHurtboxes || [], schedule.probe, scene, hitbox);
            }
        }
        state.layers.attackHitbox.addChild(hitbox);
        if (selected) {
            const trajectory = new PIXI.Graphics();
            const rawPoint = mapPointToWorld(schedule.probe, scene);
            trajectory.lineStyle(2, 0xffcf5c, 0.95);
            trajectory.moveTo(rawPoint.x, rawPoint.y);
            trajectory.lineTo(runtimeCenter.x, runtimeCenter.y);
            drawCross(trajectory, rawPoint.x, rawPoint.y, 8, 0xffcf5c);
            state.layers.attackTrajectory.addChild(trajectory);
            addAttackLabel(
                state.layers.attackTrajectory,
                runtimeCenter.x,
                runtimeCenter.y - radius - 8,
                `Skill #${geometry.skillId} · impatto F ${formatNumber(schedule.executionFrame)}`
            );
        }
    }

    function updateSchedulerUi() {
        schedulerRange.value = String(state.schedulerFrame);
        schedulerMode.classList.toggle("active", state.temporalMode);
        schedulerMode.textContent = state.temporalMode ? "Tempo attivo" : "Simula";
        schedulerPlay.textContent = state.schedulerPlaying ? "Ⅱ" : "▶";
        const firstInexactFrame = state.attackSequence && state.attackSequence.firstInexactFrame;
        const prefix = Number.isFinite(firstInexactFrame) && state.schedulerFrame >= firstInexactFrame
            ? "≥ "
            : "";
        schedulerTime.textContent = `${prefix}F ${formatNumber(state.schedulerFrame)} / ${state.schedulerMaxFrame}`;
        updateSchedulerMarkerSelection();
    }

    function setTemporalMode(enabled) {
        state.temporalMode = !!enabled;
        if (!state.temporalMode) pauseScheduler();
        updateSchedulerUi();
        drawAttackGeometry();
    }

    function pauseScheduler() {
        state.schedulerPlaying = false;
        state.schedulerLastTimestamp = 0;
        if (state.schedulerAnimationFrame) {
            cancelAnimationFrame(state.schedulerAnimationFrame);
            state.schedulerAnimationFrame = 0;
        }
        updateSchedulerUi();
    }

    function postSchedulerFrame(force = false, timestamp = performance.now()) {
        if (!force && timestamp - state.schedulerLastPostedTimestamp < 50) return;
        state.schedulerLastPostedTimestamp = timestamp;
        post({
            type: "schedulerFrameChanged",
            frame: state.schedulerFrame,
            playing: state.schedulerPlaying,
            temporalMode: state.temporalMode,
        });
    }

    function setSchedulerFrame(frame, activateTemporalMode) {
        pauseScheduler();
        state.schedulerFrame = Math.max(
            0,
            Math.min(Number(frame) || 0, state.schedulerMaxFrame)
        );
        if (activateTemporalMode) state.temporalMode = true;
        updateSchedulerUi();
        drawAttackGeometry();
    }

    function playScheduler() {
        if (!state.temporalMode) state.temporalMode = true;
        if (state.schedulerPlaying) {
            pauseScheduler();
            drawAttackGeometry();
            return;
        }
        state.schedulerPlaying = true;
        state.schedulerLastTimestamp = 0;
        updateSchedulerUi();
        state.schedulerAnimationFrame = requestAnimationFrame(updateSchedulerPlayback);
    }

    function updateSchedulerPlayback(timestamp) {
        if (!state.schedulerPlaying) return;
        if (state.schedulerLastTimestamp > 0) {
            state.schedulerFrame += (timestamp - state.schedulerLastTimestamp) * 60 / 1000;
            if (state.schedulerFrame > state.schedulerMaxFrame) state.schedulerFrame = 0;
        }
        state.schedulerLastTimestamp = timestamp;
        updateSchedulerUi();
        drawAttackGeometry();
        postSchedulerFrame(false, timestamp);
        state.schedulerAnimationFrame = requestAnimationFrame(updateSchedulerPlayback);
    }

    function addAttackLabel(container, x, y, text) {
        const label = new PIXI.Text(text, {
            fontFamily: "Segoe UI",
            fontSize: 12,
            fill: 0xf4f5f8,
            stroke: 0x101218,
            strokeThickness: 4,
        });
        label.anchor.set(0.5, 1);
        label.position.set(x, y);
        container.addChild(label);
    }

    function updateAttackInfo(geometry, target, probe) {
        const title = document.createElement("strong");
        title.textContent = `Skill #${geometry.skillId} · ${geometry.skillName}`;
        const comparison = document.createElement("div");
        comparison.textContent = geometry.comparisonText;
        const timing = document.createElement("div");
        timing.textContent = geometry.telegraphEnabled
            ? `Telegraph ${geometry.telegraphDurationFrames}f · esecuzione +${geometry.executionDelayFrames}f · offset ${formatNumber(geometry.telegraphCenterOffsetYTiles)}/${formatNumber(geometry.runtimeCenterOffsetYTiles)} tile`
            : "Telegraph non attivo: esecuzione senza ritardo aggiuntivo.";
        const repeats = document.createElement("div");
        repeats.textContent = `Avvii ×${geometry.repeatOnUseCount} · hit ×${geometry.hitRepeatCount} · intervallo ${geometry.repeatDelayMilliseconds} ms (${formatNumber(geometry.repeatDelayMilliseconds * 60 / 1000)}f @60 FPS)`;
        const limitation = document.createElement("div");
        limitation.textContent = geometry.limitationText;
        const placement = document.createElement("div");
        const origin = describeAttackOrigin(target);
        const probeAction = target.isDynamic
            ? `probe ${formatNumber(probe.x)}, ${formatNumber(probe.y)} · doppio clic per spostarlo`
            : `punto ${formatNumber(probe.x)}, ${formatNumber(probe.y)}`;
        placement.textContent = `${target.displayText || "target runtime"} · origine ${origin} · ${probeAction}`;
        attackInfo.replaceChildren(title, comparison, timing, repeats, limitation, placement);
    }

    function describeAttackOrigin(target) {
        if (target.originType === "captured") return `target catturato: ${target.originKey}`;
        if (target.originType === "anchor") return `anchor: ${target.originAnchor}`;
        if (target.originType === "role") return `ruolo: ${target.originRole}`;
        if (Number.isFinite(target.originX) && Number.isFinite(target.originY)) {
            return `${formatNumber(target.originX)}, ${formatNumber(target.originY)}`;
        }
        return target.casterRole ? `ruolo: ${target.casterRole}` : "runtime";
    }

    function formatNumber(value) {
        return Number(value).toFixed(2).replace(/\.00$/, "").replace(/(\.\d)0$/, "$1");
    }

    function resetCamera() {
        if (!state.scene || !state.app) return;
        state.fitCameraToViewport = true;
        const mapWidth = state.scene.width * state.scene.tileWidth;
        const mapHeight = state.scene.height * state.scene.tileHeight;
        const fitX = state.app.screen.width / mapWidth;
        const fitY = state.app.screen.height / mapHeight;
        state.zoom = Math.min(1.5, Math.max(0.2, Math.min(fitX, fitY)));
        state.cameraX = (mapWidth - state.app.screen.width / state.zoom) / 2;
        state.cameraY = (mapHeight - state.app.screen.height / state.zoom) / 2;
        applyCamera();
    }

    function clampCamera() {
        if (!state.scene || !state.app) return;
        const visibleWidth = state.app.screen.width / state.zoom;
        const visibleHeight = state.app.screen.height / state.zoom;
        const mapWidth = state.scene.width * state.scene.tileWidth;
        const mapHeight = state.scene.height * state.scene.tileHeight;
        state.cameraX = clampCameraAxis(state.cameraX, mapWidth, visibleWidth);
        state.cameraY = clampCameraAxis(state.cameraY, mapHeight, visibleHeight);
    }

    function clampCameraAxis(value, mapSize, visibleSize) {
        if (visibleSize >= mapSize) {
            return (mapSize - visibleSize) / 2;
        }
        return Math.max(0, Math.min(value, mapSize - visibleSize));
    }

    function applyCamera() {
        if (!state.scene || !state.app || !state.tilemap || !state.overlay) return;
        clampCamera();
        state.tilemap.scale.set(state.zoom);
        const tilemapWidth = state.app.screen.width / state.zoom;
        const tilemapHeight = state.app.screen.height / state.zoom;
        if (state.tilemap.width !== tilemapWidth || state.tilemap.height !== tilemapHeight) {
            state.tilemap.width = tilemapWidth;
            state.tilemap.height = tilemapHeight;
            state.tilemap.refresh();
        }
        state.tilemap.origin.set(state.cameraX, state.cameraY);
        state.overlay.scale.set(state.zoom);
        state.overlay.position.set(-state.cameraX * state.zoom, -state.cameraY * state.zoom);
        if (state.layers.parallax) {
            const zeroParallax = state.scene.parallaxName.startsWith("!");
            const parallax = state.layers.parallax;
            parallax.scale.set(zeroParallax ? state.zoom : 1);
            parallax.move(
                0,
                0,
                state.app.screen.width / (zeroParallax ? state.zoom : 1),
                state.app.screen.height / (zeroParallax ? state.zoom : 1)
            );
            parallax.origin.x = zeroParallax
                ? state.cameraX
                : state.scene.parallaxLoopX ? state.cameraX / 2 : 0;
            parallax.origin.y = zeroParallax
                ? state.cameraY
                : state.scene.parallaxLoopY ? state.cameraY / 2 : 0;
        }
        status.textContent = `Map ${String(state.scene.mapId).padStart(3, "0")} · zoom ${Math.round(state.zoom * 100)}% · camera ${state.cameraX.toFixed(1)}, ${state.cameraY.toFixed(1)}`;
    }

    canvas.addEventListener("pointerdown", event => {
        state.dragging = true;
        state.dragX = event.clientX;
        state.dragY = event.clientY;
        state.cameraStartX = state.cameraX;
        state.cameraStartY = state.cameraY;
        canvas.setPointerCapture(event.pointerId);
        canvas.classList.add("dragging");
    });

    canvas.addEventListener("pointermove", event => {
        if (!state.dragging) return;
        if (Math.abs(event.clientX - state.dragX) > 1 || Math.abs(event.clientY - state.dragY) > 1) {
            state.fitCameraToViewport = false;
        }
        state.cameraX = state.cameraStartX - (event.clientX - state.dragX) / state.zoom;
        state.cameraY = state.cameraStartY - (event.clientY - state.dragY) / state.zoom;
        applyCamera();
    });

    function stopDragging(event) {
        state.dragging = false;
        canvas.classList.remove("dragging");
        if (canvas.hasPointerCapture(event.pointerId)) canvas.releasePointerCapture(event.pointerId);
    }
    canvas.addEventListener("pointerup", stopDragging);
    canvas.addEventListener("pointercancel", stopDragging);
    canvas.addEventListener("dblclick", event => {
        if (!state.scene || !state.attack || !state.app) return;
        const rect = canvas.getBoundingClientRect();
        const worldX = state.cameraX + (event.clientX - rect.left) / state.zoom;
        const worldY = state.cameraY + (event.clientY - rect.top) / state.zoom;
        const point = {
            x: Math.max(0, Math.min(
                state.scene.width - 1,
                worldX / state.scene.tileWidth - 0.5
            )),
            y: Math.max(0, Math.min(
                state.scene.height - 1,
                worldY / state.scene.tileHeight - 1
            )),
        };
        const target = state.attack.target || {};
        const probes = state.attackProbeByMap[state.scene.mapId] ||
            (state.attackProbeByMap[state.scene.mapId] = {});
        const probeKey = attackReferenceKey(
            target.targetType,
            target.targetKey,
            target.targetAnchor,
            target.targetRole,
            state.attack.stepIndex,
            "target"
        );
        probes[probeKey] = point;
        configureScheduler(false);
        drawAttackGeometry();
        status.textContent = `Impatto preview · ${formatNumber(point.x)}, ${formatNumber(point.y)}`;
    });

    canvas.addEventListener("wheel", event => {
        if (!state.scene || !state.app) return;
        event.preventDefault();
        const factor = event.deltaY < 0 ? 1.12 : 1 / 1.12;
        const rect = canvas.getBoundingClientRect();
        zoomAt(factor, event.clientX - rect.left, event.clientY - rect.top);
    }, { passive: false });

    function zoomAt(factor, pointerX, pointerY) {
        if (!state.scene || !state.app) return;
        state.fitCameraToViewport = false;
        const oldZoom = state.zoom;
        const newZoom = Math.max(0.2, Math.min(4, oldZoom * factor));
        const worldX = state.cameraX + pointerX / oldZoom;
        const worldY = state.cameraY + pointerY / oldZoom;
        state.zoom = newZoom;
        state.cameraX = worldX - pointerX / newZoom;
        state.cameraY = worldY - pointerY / newZoom;
        applyCamera();
    }

    function zoomFromCenter(factor) {
        if (!state.app) return;
        zoomAt(factor, state.app.screen.width / 2, state.app.screen.height / 2);
    }

    document.getElementById("reset").addEventListener("click", resetCamera);
    document.getElementById("zoom-in").addEventListener("click", () => zoomFromCenter(1.2));
    document.getElementById("zoom-out").addEventListener("click", () => zoomFromCenter(1 / 1.2));
    schedulerMode.addEventListener("click", () => setTemporalMode(!state.temporalMode));
    schedulerPlay.addEventListener("click", playScheduler);
    schedulerRange.addEventListener("input", () => {
        pauseScheduler();
        state.temporalMode = true;
        state.schedulerFrame = Number(schedulerRange.value) || 0;
        updateSchedulerUi();
        drawAttackGeometry();
        postSchedulerFrame(true);
    });

    function toggleLayer(button) {
        if (!button) return;
        const layer = state.layers[button.dataset.layer];
        if (!layer) return;
        layer.visible = !layer.visible;
        state.layerVisibility[button.dataset.layer] = layer.visible;
        button.classList.toggle("active", layer.visible);
        button.setAttribute("aria-pressed", String(layer.visible));
    }

    function syncLayerButtons() {
        for (const button of layerButtons) {
            const visible = state.layerVisibility[button.dataset.layer] !== false;
            button.classList.toggle("active", visible);
            button.setAttribute("aria-pressed", String(visible));
        }
    }

    for (const button of layerButtons) {
        button.addEventListener("click", () => toggleLayer(button));
    }

    window.addEventListener("keydown", event => {
        if (!state.scene || event.ctrlKey || event.altKey || event.metaKey) return;
        const key = event.key.toLowerCase();
        if (key === "0") resetCamera();
        if (key === "+" || key === "=") zoomFromCenter(1.2);
        if (key === "-") zoomFromCenter(1 / 1.2);
        if (key === "g") toggleLayer(layerButtons.find(button => button.dataset.layer === "grid"));
        if (key === "r") toggleLayer(layerButtons.find(button => button.dataset.layer === "regions"));
        if (key === "a") toggleLayer(layerButtons.find(button => button.dataset.layer === "anchors"));
    });

    window.chrome.webview.addEventListener("message", event => {
        const message = event.data;
        if (message && message.type === "loadScene" && message.scene) {
            loadScene(message.scene);
        } else if (message && message.type === "clearScene") {
            showEmptyScene();
        } else if (message && message.type === "setAttackGeometry") {
            setAttackGeometry(message.attack, message.sequence);
        } else if (message && message.type === "setSchedulerFrame") {
            setSchedulerFrame(message.frame, message.activateTemporalMode);
        }
    });

    post({ type: "ready" });
})();
