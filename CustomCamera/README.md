# UCH Camera Mod

A BepInEx mod for **Ultimate Chicken Horse** that lets you customise the game camera
in real time via an in-game overlay panel and keyboard shortcuts.

---

## Requirements

| Dependency | Version | Where to get it |
|---|---|---|
| BepInEx | 5.4.x (Mono) | https://github.com/BepInEx/BepInEx/releases |
| .NET / Visual Studio | .NET 4.6 target | included in VS 2022 |

---

## Building

1. Open `UCHCameraMod.csproj` in Visual Studio (or `dotnet build` from the CLI).
2. Edit `<BepInExDir>` in the `.csproj` to point at your BepInEx folder.
3. Build — the post-build step copies `UCHCameraMod.dll` straight into `BepInEx\plugins\`.

> **First time only:** verify that `ZoomCamera` is the correct class name by opening
> `Assembly-CSharp.dll` in [dnSpy](https://github.com/dnSpy/dnSpy). Search for
> `manualZoom` or `smoothFollowCamOn` to confirm the class and field names, then
> update `Patches.cs` and `CameraModController.cs` if they differ.

---

## Usage

| Key | Action |
|---|---|
| **F6** | Toggle camera customization mode on/off |
| **F7** | Toggle `manualZoom` |
| **F8** | Toggle `smoothFollowCam` |
| **[ / ]** | Decrease / increase Field of View |
| **← → ↑ ↓** | Move camera X / Y |
| **Page Up / Page Down** | Move camera Z (closer/further) |
| **Shift + ↑ / ↓** | Increase top / bottom unit buffer |
| **Numpad 4 / 6** | Decrease / increase left buffer |
| **Numpad 7 / 9** | Decrease / increase right buffer |
| **F9** | Reset all values to defaults |

You can also drag the on-screen panel anywhere and use the sliders / text fields directly.
Settings are saved to `BepInEx/config/com.yourname.uchcameramod.cfg` automatically.

---

## Troubleshooting

**"ZoomCamera component not found"** in the BepInEx console
→ The class name is different in this build of UCH. Use dnSpy on `Assembly-CSharp.dll`
  and search for `manualZoom` to find the real class name, then update `Patches.cs`
  and `CameraModController.cs`.

**Camera snaps back every frame**
→ UCH might call its camera update in `FixedUpdate` or a coroutine instead of `Update`/
  `LateUpdate`. Add more Harmony patches in `Patches.cs` targeting those methods.

**Panel not visible**
→ Make sure F6 has been pressed. The panel only appears while the mod is active.
