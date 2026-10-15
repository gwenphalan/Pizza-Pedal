### Workshop & Dialogue Section

This section is for clarifying requirements and design decisions before implementation. All questions will be recorded here, and the answers will be integrated into the main plan.

**Q1: UI Component Confirmation**

- **Question:** The previous implementation had issues partly because it was unclear whether to use a Unity `Image` or `RawImage`. To correctly and efficiently display a `RenderTexture` (which is what our map camera will output), the standard component is `RawImage`. Is it acceptable to structure the minimap UI to use a `RawImage` for displaying the map content? A separate `Image` component can still be used for the frame/border.
- **Developer Answer:** Ok, so the `MinimapController` is placed on a `MinimapRoot` object containing an `Image` component and a `Mask` Component, the color on this `Image` component is the background color that renders. There is a `MapRenderImage` as a child to `MinimapRoot` with a `RawImage` component, THIS IS WHERE THE MAP IS RENDERED. Childed to the `MinimapRoot` is another `Image` with `Image > Maskable` disabled, called `Outline`. Childed to the `MinimapRoot` is a `PlayerIcon` which either rotates or doesnt depending on the minimap behavior.

**Q2: Required Styling Properties**

- **Question:** To ensure the `MapRendererService` is fully customizable, what specific visual elements of the map do you need to control from its Inspector? My proposed list is:
  - Map Background Color
  - Street Color
  - Street Width
  - Player Path Color (for navigation)
  - Player Path Width
- **Developer Answer:** All of those, as well as `Outline Color` and `Player Icon Color`.

---

### Map & Minimap Refactor Implementation Plan

#### 1. **Title & Overview**

- **Title:** Map & Minimap Refactor Implementation Plan
- **Summary:** This plan refactors the map rendering and minimap UI systems to establish a clear, one-way, event-driven architecture. The `MapRendererService` will be the sole authority for rendering the map and all its styling (including UI element colors), while the `MinimapController` will be a simple UI view that subscribes to updates from the service. This will fix editor-time update loops, centralize configuration, and create a robust, reusable map system.

#### 2. **Step-by-Step Audit/Verification Guide**

1.  **`MapRendererService.cs`:**
    - **Location:** `Assets/Scripts/Map/MapRendererService.cs`
    - **Verify:**
      - It must **not** have any reference to `MinimapController` or any other UI component.
      - It must have two public C# events: `public event Action<RenderTexture> OnMapUpdated;` and `public event Action<MapUIStyles> OnStyleUpdated;`.
      - All styling properties (map colors, UI colors, widths) must be public or `[SerializeField]` and trigger an update via `OnValidate`.
      - The `OnValidate` method should call a private `UpdateMapAndStyles()` method which then invokes both events. It should **not** call any external scripts.
2.  **`MinimapController.cs`:**
    - **Location:** `Assets/Scripts/UI/MinimapController.cs`
    - **Verify:**
      - It must have `[SerializeField]` references for `MapRendererService`, `RawImage` (map content), `Image` (outline), and `Image` (player icon).
      - It must subscribe to **both** `OnMapUpdated` and `OnStyleUpdated` in `OnEnable` and unsubscribe in `OnDisable`.
      - The event handlers must only apply the data they receive (texture or colors) to the appropriate UI components.
      - Its `OnValidate` method should **not** call the `MapRendererService`.

#### 3. **Core Vision & Goals**

- **Decoupling:** `MapRendererService` renders the map and defines styles; `MinimapController` displays them. They communicate only via service reference and C# events.
- **Single Source of Truth:** All configuration for the _appearance of the map content and related UI elements_ (colors, line widths, etc.) will reside exclusively in `MapRendererService`.
- **Robust Editor Preview:** Changing any style property in `MapRendererService` must immediately and reliably update the `RenderTexture` and UI colors.

#### 4. **Analysis of Pre-existing Assets/Attempts**

- **Problem:** The existing implementation has circular dependencies and split responsibilities for styling.
- **Solution:** An event-based architecture with a single source of truth for all styling properties.

#### 5. **Proposed Architecture**

```
// Defines all non-map-texture styles
public struct MapUIStyles {
    public Color outlineColor;
    public Color playerIconColor;
}

[MapRendererService]
  - Manages private Camera & RenderTexture
  - Public properties for ALL map & UI styling
  - Public Event: OnMapUpdated(RenderTexture texture)
  - Public Event: OnStyleUpdated(MapUIStyles styles)
  - OnValidate:
    - Apply styles
    - Render camera
    - Invoke OnMapUpdated
    - Invoke OnStyleUpdated
      │
      ├─(Event Subscriptions)───┐
      ▼                         ▼
[MinimapController]
  - Holds reference to MapRendererService & UI Images
  - OnEnable: Subscribes to OnMapUpdated & OnStyleUpdated
  - OnDisable: Unsubscribes
  - HandleMapUpdate(tex): rawImage.texture = tex
  - HandleStyleUpdate(styles):
    - outlineImage.color = styles.outlineColor
    - playerIconImage.color = styles.playerIconColor
```

#### 6. **Implementation Roadmap**

1.  **Refactor `MapRendererService.cs`:**
    - Add the `public struct MapUIStyles`.
    - Add `public event Action<MapUIStyles> OnStyleUpdated;`.
    - Add public properties for `outlineColor` and `playerIconColor`.
    - In the unified `UpdateMapAndStyles()` method, populate a `MapUIStyles` struct and invoke the new event.
2.  **Refactor `MinimapController.cs`:**
    - Add `[SerializeField]` references for the `outlineImage` and `playerIconImage`.
    - Subscribe to `OnStyleUpdated` in `OnEnable` and unsubscribe in `OnDisable`.
    - Create the event handler `private void HandleStyleUpdate(MapUIStyles styles)` to apply the colors.
3.  **Update Prefab/Scene:**
    - Ensure the `MinimapController` has all its new `Image` references assigned correctly based on the hierarchy you provided.

#### 7. **Detailed Script Implementation Tables**

**`MapRendererService.cs`**
| Step | Action Plan |
|---|---|
| **1. Skeleton** | Keep the existing file `Assets/Scripts/Map/MapRendererService.cs`. Add `public struct MapUIStyles { public Color outlineColor; public Color playerIconColor; }`. |
| **2. Properties** | - **Add:** `public event Action<RenderTexture> OnMapUpdated;`.<br>- **Add:** `public event Action<MapUIStyles> OnStyleUpdated;`.<br>- **Add:** `public Color outlineColor = Color.black;`.<br>- **Add:** `public Color playerIconColor = Color.red;`.<br>- **Modify:** Convert all private `[SerializeField]` style properties to `public`. |
| **3. Logic** | - **Create:** `private void UpdateAndRenderMap()` that applies map styles (streets, background), renders the camera, invokes `OnMapUpdated`, then creates a `MapUIStyles` struct, populates it from the public properties, and invokes `OnStyleUpdated`.<br>- **Refactor `OnValidate`:** Remove all existing logic. Add a single call to `UpdateAndRenderMap()` inside an `EditorApplication.delayCall`.<br>- **Refactor `LateUpdate`:** Call `UpdateAndRenderMap()` here for runtime updates. |
| **4. Preservation** | The original file will be modified in place. |

**`MinimapController.cs`**
| Step | Action Plan |
|---|---|
| **1. Skeleton** | Keep the existing file `Assets/Scripts/UI/MinimapController.cs`. |
| **2. Properties** | - **Remove:** `frameColor`, `playerIconColor`.<br>- **Add:** `[SerializeField] private MapRendererService mapService;`.<br>- **Add:** `[SerializeField] private Image outlineImage;`.<br>- **Add:** `[SerializeField] private Image playerIconImage;`.<br>- **Modify:** `mapContentImage` must be of type `RawImage`. |
| **3. Logic** | - **Remove:** `EditorUpdate()` method and `OnValidate` logic.<br>- **Implement `OnEnable`:** Subscribe to `mapService.OnMapUpdated += HandleMapUpdate;` and `mapService.OnStyleUpdated += HandleStyleUpdate;`. Request an initial update from the service.<br>- **Implement `OnDisable`:** Unsubscribe from both events.<br>- **Create `HandleMapUpdate`:** `private void HandleMapUpdate(RenderTexture mapTexture) { ... }`.<br>- **Create `HandleStyleUpdate`:** `private void HandleStyleUpdate(MapUIStyles styles) { if (outlineImage != null) outlineImage.color = styles.outlineColor; if (playerIconImage != null) playerIconImage.color = styles.playerIconColor; }`. |
| **4. Preservation** | The original file will be modified in place. |

```

```
