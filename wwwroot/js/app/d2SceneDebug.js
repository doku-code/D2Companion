// Optional D2 scene coordinate debug overlay.
// Enable with ?debugD2Controls=1 on the Gear Viewer page. The overlay
// renders ON TOP of the normal Gear Viewer — the D2 artwork, items,
// and buttons all remain visible and interactive. The overlay only
// adds:
//   * a live (x, y) readout in .d2-scene logical pixels (the same
//     coordinate space as item slots and .d2-panel-control buttons),
//   * a crosshair at the mouse position,
//   * a small toolbar to force the cube and mercenary popovers open
//     so their close-X slots can actually be measured (the previous
//     version showed coordinates over an empty scene because those
//     popovers only render when explicitly opened),
//   * a click handler that logs the coordinate pair in the exact
//     "--cx: Npx; --cy: Mpx;" form so it can be pasted straight into
//     a .d2-panel-control rule.
//
// Off by default — production users never see this.

export function initD2SceneDebug() {
  const params = new URLSearchParams(window.location.search);
  if (params.get("debugD2Controls") !== "1") return;

  let scene = document.querySelector(".d2-scene");
  let badge = null;
  let crosshair = null;
  let buttonPreview = null;
  let toolbar = null;

  function ensureScene() {
    if (scene && document.body.contains(scene)) return scene;
    scene = document.querySelector(".d2-scene");
    return scene;
  }

  function ensureOverlay() {
    if (!ensureScene()) return false;
    if (!badge || !scene.contains(badge)) {
      badge = document.createElement("div");
      badge.className = "d2-scene-debug";
      badge.textContent = "scene: —";
      scene.appendChild(badge);
    }
    if (!crosshair || !scene.contains(crosshair)) {
      crosshair = document.createElement("div");
      crosshair.className = "d2-scene-debug-crosshair";
      crosshair.style.left = "-99px";
      crosshair.style.top = "-99px";
      scene.appendChild(crosshair);
    }
    if (!buttonPreview || !scene.contains(buttonPreview)) {
      // 32×32 ghost outline matching a .d2-panel-control's size and
      // anchoring rule. Makes it obvious whether the button would
      // sit centred on the slot at the current cursor position.
      buttonPreview = document.createElement("div");
      buttonPreview.className = "d2-scene-debug-button-preview";
      buttonPreview.style.left = "-99px";
      buttonPreview.style.top = "-99px";
      scene.appendChild(buttonPreview);
    }
    if (!toolbar || !scene.contains(toolbar)) {
      toolbar = document.createElement("div");
      toolbar.className = "d2-scene-debug-toolbar";
      // Toolbar lives in the top-RIGHT of the scene; the readout is
      // in the top-LEFT. Neither overlaps the bottom-right close-X
      // slots that are the typical measurement target. The buttons
      // dispatch a CustomEvent that the gear-viewer appController
      // listens for — see bindEvents() over there. Force-opening
      // both overlays at the same time is OK; whichever popover is
      // currently set in state.* is the one that renders.
      toolbar.innerHTML = `
        <span class="d2-scene-debug-toolbar__title">debug overlay</span>
        <button type="button" data-debug-overlay="cube">Show Cube</button>
        <button type="button" data-debug-overlay="merc">Show Mercenary</button>
        <button type="button" data-debug-overlay="none">Hide</button>
      `;
      toolbar.addEventListener("click", event => {
        const btn = event.target.closest("[data-debug-overlay]");
        if (!btn) return;
        document.dispatchEvent(new CustomEvent("d2:debug:overlay", {
          detail: { which: btn.dataset.debugOverlay },
        }));
      });
      scene.appendChild(toolbar);
    }
    return true;
  }

  function toSceneCoords(event) {
    if (!ensureScene()) return null;
    const rect = scene.getBoundingClientRect();
    const scaleX = scene.offsetWidth / rect.width;
    const scaleY = scene.offsetHeight / rect.height;
    const x = (event.clientX - rect.left) * scaleX;
    const y = (event.clientY - rect.top) * scaleY;
    return { x: Math.round(x), y: Math.round(y) };
  }

  document.addEventListener("mousemove", event => {
    if (!ensureOverlay()) return;
    const coords = toSceneCoords(event);
    if (!coords) return;
    // Pointer outside the scene rect → hide crosshair and grey out.
    if (coords.x < 0 || coords.y < 0 || coords.x > 800 || coords.y > 600) {
      crosshair.style.left = "-99px";
      crosshair.style.top = "-99px";
      buttonPreview.style.left = "-99px";
      buttonPreview.style.top = "-99px";
      badge.style.opacity = "0.4";
      return;
    }
    badge.style.opacity = "1";
    badge.textContent = `scene: --cx ${coords.x}px / --cy ${coords.y}px`;
    crosshair.style.left = `${coords.x}px`;
    crosshair.style.top = `${coords.y}px`;
    // Button preview uses transform: translate(-50%, -50%) in CSS,
    // so positioning it via raw left/top centres it on the cursor —
    // same anchoring rule as a real .d2-panel-control.
    buttonPreview.style.left = `${coords.x}px`;
    buttonPreview.style.top = `${coords.y}px`;
  }, { passive: true });

  document.addEventListener("click", event => {
    // Don't log toolbar button clicks as coordinate samples.
    if (event.target.closest(".d2-scene-debug-toolbar")) return;
    const coords = toSceneCoords(event);
    if (!coords) return;
    if (coords.x < 0 || coords.y < 0 || coords.x > 800 || coords.y > 600) return;
    // Format the coordinates exactly as a .d2-panel-control rule
    // expects, so the value can be pasted straight into d2-scene.css.
    console.log(`[d2SceneDebug] --cx: ${coords.x}px; --cy: ${coords.y}px;`);
  });
}
