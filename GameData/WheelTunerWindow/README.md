Wheel Tuner (KSP 1.12.5)
========================

Wheel Tuner is a development/diagnostics utility for Kerbal Space Program 1.12.5 that lets you
inspect and (optionally) tune wheel/landing-gear module values at runtime in BOTH:
- Flight
- Vehicle Editor (VAB/SPH)

It includes:
- AppLauncher (toolbar) button to toggle the window
- Alt+W hotkey toggle
- Dirty flag tracking ([DIRTY]) when you change values
- "Apply to symmetry" propagation (mirrored/symmetry counterparts)
- Diagnostics (read-only) mode
- Export + display + copy-to-clipboard ModuleManager patch output
  - Includes scalar/bool fields AND FloatCurve keys (steeringCurve / torqueCurve)


INSTALLATION
------------
Install via CKAN 
	or 
Copy the folder WheelTunerWindow into your GameData folder


OPENING / CLOSING
-----------------
- Toolbar: click the Wheel Tuner AppLauncher icon (Editor and Flight)
- Hotkey: Alt + W (toggle)
- The window starts hidden by default.
- The plugin does not persist across scene changes; it is created in Editor/Flight and destroyed
  when you leave those scenes.


BASIC USE
---------
1) Open the window (toolbar button or Alt+W).
2) (Optional) Enable/disable:
   - Auto refresh list: updates wheel list periodically
   - Apply to symmetry: applies changes to symmetry counterparts automatically
   - Diagnostics (read-only): shows values only, disables editing controls

3) Expand a wheel entry to view/edit modules.


MODULES SUPPORTED
-----------------
Wheel Tuner looks for parts containing ModuleWheelBase, and then shows associated modules if present:
- ModuleWheelBase
- ModuleWheelSuspension
- ModuleWheelSteering
- ModuleWheelMotor
- ModuleWheelBrakes
- ModuleWheelDeployment
- ModuleWheelDamage

Note: Some fields may not be exposed as public members depending on the module/build. If a field
cannot be read/written via reflection, it may show default/unchanged behavior.


DIRTY FLAGS ([DIRTY])
---------------------
- Any time you change a value, that part is marked as [DIRTY].
- If "Apply to symmetry" is enabled, symmetry counterparts are also marked [DIRTY].
- Use:
  - Rebuild: refreshes wheel internals and clears dirty flags
  - Clean: clears dirty flags without rebuilding


REBUILD (IMPORTANT)
-------------------
- Rebuild uses safe, best-effort internal update methods (where present).
- It does NOT re-run lifecycle callbacks like OnStart/OnAwake (unsafe to call manually).
- Use Rebuild when:
  - suspension/friction/steering feels “stale” after edits
  - visual/physics behavior doesn’t immediately reflect changes


DIAGNOSTICS (READ-ONLY) MODE
----------------------------
Enable "Diagnostics (read-only)" to prevent accidental edits.
In this mode:
- numeric sliders/text boxes are replaced by readonly value displays
- toggles become readonly value displays
You can still:
- Refresh Wheels
- Rebuild/Clean
- Build/Copy MM patch output


SYMMETRY SUPPORT
----------------
When "Apply to symmetry" is enabled:
- Changes to a module field are mirrored to symmetry counterparts of that part
- Matching is done by module type + moduleName (with a type fallback)


MODULEMANAGER PATCH EXPORT
--------------------------
The patch panel can:
- Build and display an MM patch in the window
- Copy the patch to clipboard

Options:
- "Build patch for ALL wheels" (otherwise it exports the currently selected wheel/part)

What is exported:
- Scalar/bool/enum fields shown in the UI
- FloatCurve keys for:
  - ModuleWheelSteering: steeringCurve
  - ModuleWheelMotor: torqueCurve

Curve export strategy:
- Deletes the existing curve node and recreates it with key lines:
  key = time value inTangent outTangent

To use the patch:
1) Click "Build MM Patch"
2) Click "Copy Patch"
3) Paste into a .cfg file, e.g.:
   GameData/YourMod/WheelTunerPatch.cfg
4) Restart KSP


NOTES / LIMITATIONS
-------------------
- This is a tuning/dev tool, not intended for normal gameplay.
- Not all wheel fields are safe to change at runtime; use Rebuild as needed.
- Export output is grouped by internal part name; identical parts share a patch block.
- Values changed in-game are not persisted automatically; use MM export to make them permanent.


TROUBLESHOOTING
---------------
- Window doesn't show:
  - Ensure you're in Flight or VAB/SPH
  - Click the AppLauncher icon, or press Alt+W

- Sliders/buttons feel ignored:
  - Ensure Diagnostics (read-only) is OFF

- Changes don't “take”:
  - Hit Rebuild
  - Some values require wheel collider refresh and may still be limited by KSP internals
