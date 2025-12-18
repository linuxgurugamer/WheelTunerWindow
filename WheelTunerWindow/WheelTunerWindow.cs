using ClickThroughFix;
using KSP.UI.Screens;
using ModuleWheels;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using ToolbarControl_NS;
using UnityEngine;

// WheelTunerWindow.cs (clean, no duplicate methods)
// KSP 1.12.5 runtime wheel tuning window with:
// - AppLauncher button (Editor + Flight)
// - Dirty flag per part + Apply-to-symmetry propagation
// - Read-only diagnostics mode (no edits)
// - Export ModuleManager patch + display + copy to clipboard
//

namespace WheelTunerWindow
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class WheelTunerWindowFlight : MonoBehaviour
    {
        private WheelTunerWindow _impl;
        public void Start() => _impl = gameObject.AddComponent<WheelTunerWindow>();
        public void OnDestroy() { if (_impl != null) Destroy(_impl); }
    }

    [KSPAddon(KSPAddon.Startup.EditorAny, false)]
    public class WheelTunerWindowEditor : MonoBehaviour
    {
        private WheelTunerWindow _impl;
        public void Start() => _impl = gameObject.AddComponent<WheelTunerWindow>();
        public void OnDestroy() { if (_impl != null) Destroy(_impl); }
    }

    public class WheelTunerWindow : MonoBehaviour
    {
        private const string LogTag = "[WheelTuner] ";

        private Rect _windowRect = new Rect(250, 80, 740, 820);
        private int _windowId;
        private bool _visible = false; // start hidden
        private Vector2 _scroll;

        // Top-level toggles
        private bool _autoRefreshList = true;
        private bool _applyToSymmetry = true;
        private bool _diagMode = false;

        // Selection + expand state
        private uint _selectedPartId = 0;
        private readonly Dictionary<uint, bool> _expandedByPartId = new Dictionary<uint, bool>();

        // Dirty tracking
        private readonly Dictionary<uint, bool> _dirtyByPartId = new Dictionary<uint, bool>();

        // Wheel cache
        private double _nextRefreshTime;
        private List<ModuleWheelBase> _wheelBases = new List<ModuleWheelBase>();

        // ModuleManager patch UI
        private bool _patchAllWheels = false;
        private bool _showPatch = false;
        private Vector2 _patchScroll;
        private string _mmPatchText = "";

        // AppLauncher

        // UI parsing + temp state
        private static readonly CultureInfo CI = CultureInfo.InvariantCulture;
        private readonly Dictionary<int, Dictionary<string, string>> _tempStrings = new Dictionary<int, Dictionary<string, string>>();
        private readonly Dictionary<int, Dictionary<string, float>> _tempFloats = new Dictionary<int, Dictionary<string, float>>();

        public void Awake()
        {
            _windowId = (int)(DateTime.UtcNow.Ticks & 0x7FFFFFFF);
        }

        public void Start()
        {
            InitToolbar();
            RefreshWheelList();
        }

        public void OnDestroy()
        {
            _tempStrings.Clear();
            _tempFloats.Clear();
            _expandedByPartId.Clear();
            _dirtyByPartId.Clear();
            _wheelBases.Clear();
        }

        public void Update()
        {
            // Alt+W toggle
            if ((Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) && Input.GetKeyDown(KeyCode.W))
                _visible = !_visible;

            // periodic refresh
            if (_autoRefreshList && Planetarium.GetUniversalTime() > _nextRefreshTime)
            {
                _nextRefreshTime = Planetarium.GetUniversalTime() + 1.0;
                RefreshWheelList();
            }
        }

        public void OnGUI()
        {
            if (!_visible) return;
            _windowRect = ClickThruBlocker.GUILayoutWindow(_windowId, _windowRect, DrawWindow, "Wheel Tuner (KSP 1.12.5)");
        }

        // =========================
        // Window UI
        // =========================

        private void DrawWindow(int id)
        {
            GUILayout.BeginVertical();

            DrawHeader();
            DrawPatchPanel();
            DrawWheelList();

            GUILayout.EndVertical();
            GUI.DragWindow();
        }

        private void DrawHeader()
        {
            GUILayout.BeginHorizontal();

            if (GUILayout.Button("Refresh Wheels", GUILayout.Width(130))) RefreshWheelList();
            _autoRefreshList = GUILayout.Toggle(_autoRefreshList, "Auto refresh list", GUILayout.Width(140));
            _applyToSymmetry = GUILayout.Toggle(_applyToSymmetry, "Apply to symmetry", GUILayout.Width(140));
            _diagMode = GUILayout.Toggle(_diagMode, "Diagnostics (read-only)", GUILayout.Width(170));

            GUILayout.FlexibleSpace();
            GUILayout.Label("Alt+W", GUILayout.Width(50));
            if (GUILayout.Button("Close", GUILayout.Width(70))) _visible = false;

            GUILayout.EndHorizontal();
            GUILayout.Space(6);
        }

        private void DrawPatchPanel()
        {
            GUILayout.BeginVertical("box");
            GUILayout.BeginHorizontal();

            _patchAllWheels = GUILayout.Toggle(_patchAllWheels, "Build patch for ALL wheels", GUILayout.Width(220));

            if (GUILayout.Button("Build MM Patch", GUILayout.Width(130)))
            {
                _mmPatchText = BuildModuleManagerPatch(_patchAllWheels);
                _showPatch = true;
            }

            if (GUILayout.Button("Copy Patch", GUILayout.Width(110)))
            {
                if (string.IsNullOrEmpty(_mmPatchText))
                    _mmPatchText = BuildModuleManagerPatch(_patchAllWheels);

                GUIUtility.systemCopyBuffer = _mmPatchText ?? "";
            }

            _showPatch = GUILayout.Toggle(_showPatch, "Show patch", GUILayout.Width(110));

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            if (_showPatch)
            {
                _patchScroll = GUILayout.BeginScrollView(_patchScroll, GUILayout.Height(150));
                _mmPatchText = GUILayout.TextArea(_mmPatchText ?? "", GUILayout.ExpandHeight(true));
                GUILayout.EndScrollView();
            }

            GUILayout.EndVertical();
            GUILayout.Space(6);
        }

        private void DrawWheelList()
        {
            if (_wheelBases.Count == 0)
            {
                GUILayout.Label("No ModuleWheelBase modules found on the current craft.");
                GUILayout.Label("Tip: in Editor, ensure a craft is loaded; in Flight, ensure the active vessel has wheels/gear.");
                return;
            }

            _scroll = GUILayout.BeginScrollView(_scroll);

            foreach (var wb in _wheelBases)
            {
                if (wb == null || wb.part == null) continue;

                var p = wb.part;
                uint pid = p.persistentId;
                bool expanded = GetExpanded(pid);
                bool dirty = IsDirty(p);
                bool selected = (_selectedPartId == pid);

                string partTitle = (p.partInfo != null ? p.partInfo.title : p.name);
                bool grounded = SafeGetBool(wb, "isGrounded");
                bool broken = SafeGetBool(wb, "isBroken");

                GUILayout.BeginVertical("box");
                GUILayout.BeginHorizontal();

                if (GUILayout.Button(expanded ? "▼" : "►", GUILayout.Width(24)))
                {
                    SetExpanded(pid, !expanded);
                    if (!expanded) _selectedPartId = pid;
                }

                GUILayout.Label(partTitle, GUILayout.ExpandWidth(true));

                string state = $"Grounded={grounded} Broken={broken}"
                             + (dirty ? "  [DIRTY]" : "")
                             + (selected ? "  [SELECTED]" : "");

                var oldColor = GUI.color;
                if (dirty) GUI.color = Color.red;
                GUILayout.Label(state, GUILayout.Width(360));
                GUI.color = oldColor;

                if (GUILayout.Button("Select", GUILayout.Width(70))) _selectedPartId = pid;

                if (GUILayout.Button("Rebuild", GUILayout.Width(80)))
                {
                    RebuildWheel(wb);
                    ClearDirtyForPartAndSymmetry(p);
                }

                if (GUILayout.Button("Clean", GUILayout.Width(60)))
                {
                    ClearDirtyForPartAndSymmetry(p);
                }

                GUILayout.EndHorizontal();

                if (expanded)
                    DrawWheelSections(wb);

                GUILayout.EndVertical();
                GUILayout.Space(6);
            }

            GUILayout.EndScrollView();
        }

        private void DrawWheelSections(ModuleWheelBase wb)
        {
            var p = wb.part;

            DrawWheelBaseSection(wb);

            var sus = p.FindModuleImplementing<ModuleWheelSuspension>();
            if (sus != null) DrawSuspensionSection(wb, sus);

            var steer = p.FindModuleImplementing<ModuleWheelSteering>();
            if (steer != null) DrawSteeringSection(wb, steer);

            var motor = p.FindModuleImplementing<ModuleWheelMotor>();
            if (motor != null) DrawMotorSection(wb, motor);

            var brakes = p.FindModuleImplementing<ModuleWheelBrakes>();
            if (brakes != null) DrawBrakesSection(wb, brakes);

            var dep = p.FindModuleImplementing<ModuleWheelDeployment>();
            if (dep != null) DrawDeploymentSection(wb, dep);

            var dmg = p.FindModuleImplementing<ModuleWheelDamage>();
            if (dmg != null) DrawDamageSection(wb, dmg);
        }

        // =========================
        // Module sections
        // =========================

        private void DrawWheelBaseSection(ModuleWheelBase wb)
        {
            GUILayout.Label("ModuleWheelBase", BoldLabel());
            GUILayout.BeginVertical("box");

            DrawReadonly("Part (internal)", GetPartInternalName(wb.part));
            DrawReadonly("PersistentId", wb.part.persistentId.ToString());

            DrawEditableFloatOrReadonly(wb, "radius", "radius", 0.05f, 10.0f);
            DrawEditableFloatOrReadonly(wb, "mass", "mass", 0.01f, 500f);
            DrawEditableFloatOrReadonly(wb, "frictionMultiplier", "frictionMultiplier", 0.1f, 5.0f);

            GUILayout.EndVertical();
        }

        private void DrawSuspensionSection(ModuleWheelBase wb, ModuleWheelSuspension sus)
        {
            GUILayout.Label("ModuleWheelSuspension", BoldLabel());
            GUILayout.BeginVertical("box");

            DrawEditableFloatOrReadonly(sus, "suspensionDistance", "suspensionDistance", 0.01f, 2.0f);
            DrawEditableFloatOrReadonly(sus, "targetPosition", "targetPosition", 0.0f, 1.0f);
            DrawEditableFloatOrReadonly(sus, "springRatio", "springRatio", 0.1f, 50.0f);
            DrawEditableFloatOrReadonly(sus, "damperRatio", "damperRatio", 0.0f, 50.0f);
            DrawEditableFloatOrReadonly(sus, "antiRoll", "antiRoll", 0.0f, 5.0f);

            DrawEditableBoolOrReadonly(sus, "useAutoSpring", "useAutoSpring");
            DrawEditableBoolOrReadonly(sus, "autoSpringDamper", "autoSpringDamper");

            if (!_diagMode)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Apply (Suspension)", GUILayout.Width(150)))
                {
                    RebuildWheel(wb);
                    ClearDirtyForPartAndSymmetry(wb.part);
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }

            GUILayout.EndVertical();
        }

        private void DrawSteeringSection(ModuleWheelBase wb, ModuleWheelSteering steer)
        {
            GUILayout.Label("ModuleWheelSteering", BoldLabel());
            GUILayout.BeginVertical("box");

            DrawEditableBoolOrReadonly(steer, "steeringEnabled", "steeringEnabled");
            DrawEditableFloatOrReadonly(steer, "steeringResponse", "steeringResponse", 0.1f, 20.0f);

            GUILayout.Space(4);
            GUILayout.Label("Steering Curve (preview @ speed m/s -> deg):");
            DrawCurvePreview(steer, "steeringCurve", new float[] { 0f, 5f, 10f, 20f, 30f, 60f });

            if (!_diagMode)
            {
                float scale = GetTempFloat(steer, "steeringCurveScale", 1f);
                scale = LabeledSlider("Scale curve", scale, 0.1f, 2.0f);
                SetTempFloat(steer, "steeringCurveScale", scale);

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Apply (Steering)", GUILayout.Width(140)))
                {
                    bool changed = ScaleFloatCurve(steer, "steeringCurve", scale);
                    SetTempFloat(steer, "steeringCurveScale", 1f);

                    if (changed) MarkDirtyForPartAndSymmetry(wb.part);

                    RebuildWheel(wb);
                    ClearDirtyForPartAndSymmetry(wb.part);
                }
                if (GUILayout.Button("Reset scale=1", GUILayout.Width(120)))
                    SetTempFloat(steer, "steeringCurveScale", 1f);

                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }

            GUILayout.EndVertical();
        }

        private void DrawMotorSection(ModuleWheelBase wb, ModuleWheelMotor motor)
        {
            GUILayout.Label("ModuleWheelMotor", BoldLabel());
            GUILayout.BeginVertical("box");

            DrawEditableBoolOrReadonly(motor, "motorEnabled", "motorEnabled");
            DrawEditableFloatOrReadonly(motor, "maxTorque", "maxTorque", 0.0f, 500.0f);

            GUILayout.Space(4);
            GUILayout.Label("Torque Curve (preview @ speed m/s -> torque):");
            DrawCurvePreview(motor, "torqueCurve", new float[] { 0f, 5f, 10f, 20f, 30f, 60f });

            if (!_diagMode)
            {
                float scale = GetTempFloat(motor, "torqueCurveScale", 1f);
                scale = LabeledSlider("Scale curve", scale, 0.1f, 3.0f);
                SetTempFloat(motor, "torqueCurveScale", scale);

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Apply (Motor)", GUILayout.Width(120)))
                {
                    bool changed = ScaleFloatCurve(motor, "torqueCurve", scale);
                    SetTempFloat(motor, "torqueCurveScale", 1f);

                    if (changed) MarkDirtyForPartAndSymmetry(wb.part);

                    RebuildWheel(wb);
                    ClearDirtyForPartAndSymmetry(wb.part);
                }
                if (GUILayout.Button("Reset scale=1", GUILayout.Width(120)))
                    SetTempFloat(motor, "torqueCurveScale", 1f);

                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }

            GUILayout.EndVertical();
        }

        private void DrawBrakesSection(ModuleWheelBase wb, ModuleWheelBrakes brakes)
        {
            GUILayout.Label("ModuleWheelBrakes", BoldLabel());
            GUILayout.BeginVertical("box");

            DrawEditableBoolOrReadonly(brakes, "brakeEnabled", "brakeEnabled");
            DrawEditableFloatOrReadonly(brakes, "maxBrakeTorque", "maxBrakeTorque", 0.0f, 2000.0f);
            DrawEditableFloatOrReadonly(brakes, "brakeResponse", "brakeResponse", 0.1f, 20.0f);

            if (!_diagMode)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Apply (Brakes)", GUILayout.Width(120)))
                {
                    RebuildWheel(wb);
                    ClearDirtyForPartAndSymmetry(wb.part);
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }

            GUILayout.EndVertical();
        }

        private void DrawDeploymentSection(ModuleWheelBase wb, ModuleWheelDeployment dep)
        {
            GUILayout.Label("ModuleWheelDeployment", BoldLabel());
            GUILayout.BeginVertical("box");

            DrawEditableBoolOrReadonly(dep, "retractable", "retractable");
            DrawEditableFloatOrReadonly(dep, "deploySpeed", "deploySpeed", 0.1f, 20.0f);
            DrawEditableFloatOrReadonly(dep, "deployedPosition", "deployedPosition", 0.0f, 1.0f);
            DrawEditableFloatOrReadonly(dep, "retractedPosition", "retractedPosition", 0.0f, 1.0f);

            if (!_diagMode)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Apply (Deployment)", GUILayout.Width(150)))
                {
                    RebuildWheel(wb);
                    ClearDirtyForPartAndSymmetry(wb.part);
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }

            GUILayout.EndVertical();
        }

        private void DrawDamageSection(ModuleWheelBase wb, ModuleWheelDamage dmg)
        {
            GUILayout.Label("ModuleWheelDamage", BoldLabel());
            GUILayout.BeginVertical("box");

            DrawEditableFloatOrReadonly(dmg, "stressTolerance", "stressTolerance", 0.0f, 20000.0f);
            DrawEditableFloatOrReadonly(dmg, "impactTolerance", "impactTolerance", 0.0f, 20000.0f);
            DrawEditableBoolOrReadonly(dmg, "repairable", "repairable");

            GUILayout.EndVertical();
        }

        // =========================
        // Discovery
        // =========================

        private void RefreshWheelList()
        {
            try
            {
                IEnumerable<Part> parts = GetCurrentParts();
                if (parts == null)
                {
                    _wheelBases = new List<ModuleWheelBase>();
                    return;
                }

                var bases = new List<ModuleWheelBase>();
                foreach (var p in parts)
                {
                    if (p == null) continue;
                    bases.AddRange(p.FindModulesImplementing<ModuleWheelBase>());
                }

                _wheelBases = bases
                    .Where(b => b != null && b.part != null)
                    .OrderBy(b => b.part.partInfo != null ? b.part.partInfo.title : b.part.name)
                    .ToList();

                if (_selectedPartId != 0 && !_wheelBases.Any(b => b.part.persistentId == _selectedPartId))
                    _selectedPartId = 0;
            }
            catch (Exception ex)
            {
                Debug.LogError(LogTag + "RefreshWheelList failed: " + ex);
                _wheelBases = new List<ModuleWheelBase>();
            }
        }

        private IEnumerable<Part> GetCurrentParts()
        {
            if (HighLogic.LoadedSceneIsFlight)
            {
                var v = FlightGlobals.ActiveVessel;
                return v != null ? v.Parts : null;
            }

            if (HighLogic.LoadedSceneIsEditor)
            {
                var ship = (EditorLogic.fetch != null) ? EditorLogic.fetch.ship : null;
                return ship != null ? ship.parts : null;
            }

            return null;
        }

        // =========================
        // Rebuild (safe)
        // =========================

        private void RebuildWheel(ModuleWheelBase wb)
        {
            if (wb == null || wb.part == null) return;

            try
            {
                InvokeIfExists(wb, "UpdateWheel");
                InvokeIfExists(wb, "SetupWheel");
                InvokeIfExists(wb, "SetupWheelCollider");
                InvokeIfExists(wb, "UpdateSuspension");
                InvokeIfExists(wb, "UpdateFriction");

                var p = wb.part;
                for (int i = 0; i < p.Modules.Count; i++)
                {
                    var m = p.Modules[i];
                    if (m == null) continue;
                    InvokeIfExists(m, "Update");
                    InvokeIfExists(m, "FixedUpdate");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError(LogTag + "RebuildWheel failed: " + ex);
            }
        }

        // =========================
        // Dirty + symmetry
        // =========================

        private bool IsDirty(Part p)
        {
            if (p == null) return false;
            return _dirtyByPartId.TryGetValue(p.persistentId, out var d) && d;
        }

        private void MarkDirty(Part p)
        {
            if (p == null) return;
            _dirtyByPartId[p.persistentId] = true;
        }

        private void ClearDirty(Part p)
        {
            if (p == null) return;
            _dirtyByPartId[p.persistentId] = false;
        }

        private void MarkDirtyForPartAndSymmetry(Part root)
        {
            if (root == null) return;
            MarkDirty(root);

            if (_applyToSymmetry)
                foreach (var p in GetSymmetryGroup(root)) MarkDirty(p);
        }

        private void ClearDirtyForPartAndSymmetry(Part root)
        {
            if (root == null) return;
            ClearDirty(root);

            if (_applyToSymmetry)
                foreach (var p in GetSymmetryGroup(root)) ClearDirty(p);
        }

        private IEnumerable<Part> GetSymmetryGroup(Part root)
        {
            if (root == null) yield break;

            yield return root;

            var cps = root.symmetryCounterparts;
            if (cps == null) yield break;

            for (int i = 0; i < cps.Count; i++)
            {
                var p = cps[i];
                if (p != null) yield return p;
            }
        }

        private PartModule FindMatchingModuleOnPart(Part targetPart, PartModule sourceModule)
        {
            if (targetPart == null || sourceModule == null) return null;

            string wantName = sourceModule.moduleName;
            Type wantType = sourceModule.GetType();

            for (int i = 0; i < targetPart.Modules.Count; i++)
            {
                var m = targetPart.Modules[i];
                if (m == null) continue;
                if (m.moduleName == wantName && m.GetType() == wantType)
                    return m;
            }

            for (int i = 0; i < targetPart.Modules.Count; i++)
            {
                var m = targetPart.Modules[i];
                if (m == null) continue;
                if (m.GetType() == wantType)
                    return m;
            }

            return null;
        }

        // Single SetValue implementation (set + symmetry + dirty)
        private bool SetValue(object moduleObj, string name, object value)
        {
            if (moduleObj == null) return false;

            PartModule pm = moduleObj as PartModule;
            Part sourcePart = pm != null ? pm.part : null;

            bool changedAny = false;

            changedAny |= SafeSet(moduleObj, name, value);

            if (_applyToSymmetry && pm != null && sourcePart != null)
            {
                foreach (var p in GetSymmetryGroup(sourcePart))
                {
                    if (p == null || p == sourcePart) continue;

                    var match = FindMatchingModuleOnPart(p, pm);
                    if (match == null) continue;

                    bool changed = SafeSet(match, name, value);
                    if (changed)
                    {
                        changedAny = true;
                        MarkDirty(p);
                    }
                }
            }

            if (changedAny && sourcePart != null)
                MarkDirty(sourcePart);

            return changedAny;
        }

        // =========================
        // Read-only / editable controls
        // =========================

        private GUIStyle BoldLabel()
        {
            var s = new GUIStyle(GUI.skin.label);
            s.fontStyle = FontStyle.Bold;
            return s;
        }

        private void DrawReadonly(string label, string value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(200));
            GUILayout.Label(value);
            GUILayout.EndHorizontal();
        }

        private void DrawEditableBoolOrReadonly(object module, string fieldName, string label)
        {
            if (_diagMode)
            {
                DrawReadonly(label, SafeGetBool(module, fieldName).ToString());
                return;
            }

            bool current = SafeGetBool(module, fieldName);
            bool next = GUILayout.Toggle(current, label);
            if (next != current)
                SetValue(module, fieldName, next);
        }

        private void DrawEditableFloatOrReadonly(object module, string key, string label, float min, float max)
        {
            if (_diagMode)
            {
                DrawReadonly(label, SafeGetFloat(module, key).ToString("0.###", CI));
                return;
            }

            DrawEditableFloat(module, key, label, min, max);
        }

        // Fixed float editor (slider/+/- work; textbox no longer overrides slider changes)
        private void DrawEditableFloat(object module, string key, string label, float min, float max)
        {
            float current = SafeGetFloat(module, key);

            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(200));

            string textKey = module.GetHashCode() + ":" + key + ":text";
            string txt = GetTempString(module, textKey, current.ToString("0.###", CI));
            string newTxt = GUILayout.TextField(txt, GUILayout.Width(90));
            if (newTxt != txt) SetTempString(module, textKey, newTxt);

            float currentClamped = Mathf.Clamp(current, min, max);
            float newSlider = GUILayout.HorizontalSlider(currentClamped, min, max, GUILayout.Width(240));

            bool minus = GUILayout.Button("-", GUILayout.Width(24));
            bool plus = GUILayout.Button("+", GUILayout.Width(24));

            float step = (max - min) * 0.01f;
            if (minus) newSlider = Mathf.Clamp(currentClamped - step, min, max);
            if (plus) newSlider = Mathf.Clamp(currentClamped + step, min, max);

            GUILayout.Label(current.ToString("0.###", CI), GUILayout.Width(80));
            GUILayout.EndHorizontal();

            bool sliderChanged = !NearlyEqual(newSlider, currentClamped) || minus || plus;
            if (sliderChanged)
            {
                if (!NearlyEqual(newSlider, current))
                    SetValue(module, key, newSlider);

                SetTempString(module, textKey, newSlider.ToString("0.###", CI));
                return;
            }

            if (TryParseFloat(newTxt, out float parsed))
            {
                parsed = Mathf.Clamp(parsed, min, max);
                if (!NearlyEqual(parsed, current))
                    SetValue(module, key, parsed);
            }
        }

        private float LabeledSlider(string label, float value, float min, float max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(200));
            float v = GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(240));
            GUILayout.Label(v.ToString("0.###", CI), GUILayout.Width(80));
            GUILayout.EndHorizontal();
            return v;
        }

        private static bool TryParseFloat(string s, out float v)
            => float.TryParse(s, NumberStyles.Float, CI, out v);

        private static bool NearlyEqual(float a, float b)
            => Mathf.Abs(a - b) < 0.0005f;

        // =========================
        // FloatCurve helpers
        // =========================

        private void DrawCurvePreview(object module, string curveName, float[] speeds)
        {
            var curve = SafeGet(module, curveName);
            if (curve == null)
            {
                GUILayout.Label($"(No {curveName} exposed)");
                return;
            }

            var eval = curve.GetType().GetMethod("Evaluate", new[] { typeof(float) });
            if (eval == null)
            {
                GUILayout.Label($"({curveName} has no Evaluate(float))");
                return;
            }

            GUILayout.BeginHorizontal();
            foreach (var s in speeds)
            {
                float val = 0f;
                try { val = Convert.ToSingle(eval.Invoke(curve, new object[] { s }), CI); }
                catch { }
                GUILayout.Label($"{s:0}->{val:0.#}", GUILayout.Width(72));
            }
            GUILayout.EndHorizontal();
        }

        private bool ScaleFloatCurve(object moduleObj, string curveName, float scale)
        {
            if (moduleObj == null) return false;
            if (NearlyEqual(scale, 1f)) return false;

            bool anyChanged = false;

            PartModule pm = moduleObj as PartModule;
            Part sourcePart = pm != null ? pm.part : null;

            anyChanged |= ScaleFloatCurveOnObject(moduleObj, curveName, scale);

            if (_applyToSymmetry && pm != null && sourcePart != null)
            {
                foreach (var p in GetSymmetryGroup(sourcePart))
                {
                    if (p == null || p == sourcePart) continue;

                    var match = FindMatchingModuleOnPart(p, pm);
                    if (match == null) continue;

                    bool changed = ScaleFloatCurveOnObject(match, curveName, scale);
                    if (changed)
                    {
                        anyChanged = true;
                        MarkDirty(p);
                    }
                }
            }

            if (anyChanged && sourcePart != null)
                MarkDirty(sourcePart);

            return anyChanged;
        }

        private bool ScaleFloatCurveOnObject(object moduleObj, string curveName, float scale)
        {
            var curve = SafeGet(moduleObj, curveName);
            if (curve == null) return false;

            object animCurveObj = SafeGet(curve, "Curve") ?? SafeGet(curve, "curve");
            if (animCurveObj is AnimationCurve ac)
            {
                var keys = ac.keys;
                for (int i = 0; i < keys.Length; i++)
                {
                    keys[i].value *= scale;
                    keys[i].inTangent *= scale;
                    keys[i].outTangent *= scale;
                }
                ac.keys = keys;

                SafeSet(curve, "Curve", ac);
                SafeSet(curve, "curve", ac);
                return true;
            }

            var f = curve.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(x => typeof(AnimationCurve).IsAssignableFrom(x.FieldType));

            if (f != null)
            {
                var inner = f.GetValue(curve) as AnimationCurve;
                if (inner != null)
                {
                    var keys = inner.keys;
                    for (int i = 0; i < keys.Length; i++)
                    {
                        keys[i].value *= scale;
                        keys[i].inTangent *= scale;
                        keys[i].outTangent *= scale;
                    }
                    inner.keys = keys;
                    f.SetValue(curve, inner);
                    return true;
                }
            }

            return false;
        }

        // =========================
        // ModuleManager patch builder
        // =========================

        private string BuildModuleManagerPatch(bool allWheels)
        {
            try
            {
                var targets = new List<Part>();

                if (allWheels)
                {
                    foreach (var wb in _wheelBases)
                        if (wb != null && wb.part != null)
                            targets.Add(wb.part);
                }
                else
                {
                    var wb = _wheelBases.FirstOrDefault(x => x != null && x.part != null && x.part.persistentId == _selectedPartId);
                    if (wb != null && wb.part != null) targets.Add(wb.part);
                    else return "// No selected wheel. Expand a wheel or click Select, then build the patch.\n";
                }

                var groups = targets
                    .GroupBy(p => GetPartInternalName(p))
                    .OrderBy(g => g.Key)
                    .ToList();

                var sb = new System.Text.StringBuilder();
                sb.AppendLine("// Generated by Wheel Tuner (KSP 1.12.5)");
                sb.AppendLine("// Paste into: GameData/YourMod/WheelTunerPatch.cfg");
                sb.AppendLine("// NOTE: Curves are not exported here (only scalar/bool fields).");
                sb.AppendLine();

                foreach (var g in groups)
                {
                    string partName = g.Key;
                    if (string.IsNullOrEmpty(partName)) continue;

                    Part p = g.FirstOrDefault();
                    if (p == null) continue;

                    sb.AppendLine($"@PART[{partName}]");
                    sb.AppendLine("{");

                    var wb = p.FindModuleImplementing<ModuleWheelBase>();
                    if (wb != null)
                        AppendModuleBlock(sb, "ModuleWheelBase", new[] { FieldLine(wb, "radius"), FieldLine(wb, "mass"), FieldLine(wb, "frictionMultiplier") });

                    var sus = p.FindModuleImplementing<ModuleWheelSuspension>();
                    if (sus != null)
                        AppendModuleBlock(sb, "ModuleWheelSuspension", new[]
                        {
                        FieldLine(sus, "suspensionDistance"),
                        FieldLine(sus, "targetPosition"),
                        FieldLine(sus, "springRatio"),
                        FieldLine(sus, "damperRatio"),
                        FieldLine(sus, "antiRoll"),
                        FieldLine(sus, "useAutoSpring"),
                        FieldLine(sus, "autoSpringDamper"),
                    });

                    var steer = p.FindModuleImplementing<ModuleWheelSteering>();
                    if (steer != null)
                    {
                        var steerCurveObj = SafeGet(steer, "steeringCurve"); // FloatCurve object
                        AppendModuleBlock(sb, "ModuleWheelSteering", new[]
                        {
                            FieldLine(steer, "steeringEnabled"),
                            FieldLine(steer, "steeringResponse"),
                            BuildFloatCurvePatchChunk("steeringCurve", steerCurveObj),
                        });
                    }


                    var motor = p.FindModuleImplementing<ModuleWheelMotor>();
                    if (motor != null)
                    {
                        var torqueCurveObj = SafeGet(motor, "torqueCurve"); // FloatCurve object
                        AppendModuleBlock(sb, "ModuleWheelMotor", new[]
                        {
                            FieldLine(motor, "motorEnabled"),
                            FieldLine(motor, "maxTorque"),
                            BuildFloatCurvePatchChunk("torqueCurve", torqueCurveObj),
                        });
                    }

                    var brakes = p.FindModuleImplementing<ModuleWheelBrakes>();
                    if (brakes != null)
                        AppendModuleBlock(sb, "ModuleWheelBrakes", new[] { FieldLine(brakes, "brakeEnabled"), FieldLine(brakes, "maxBrakeTorque"), FieldLine(brakes, "brakeResponse") });

                    var dep = p.FindModuleImplementing<ModuleWheelDeployment>();
                    if (dep != null)
                        AppendModuleBlock(sb, "ModuleWheelDeployment", new[] { FieldLine(dep, "retractable"), FieldLine(dep, "deploySpeed"), FieldLine(dep, "deployedPosition"), FieldLine(dep, "retractedPosition") });

                    var dmg = p.FindModuleImplementing<ModuleWheelDamage>();
                    if (dmg != null)
                        AppendModuleBlock(sb, "ModuleWheelDamage", new[] { FieldLine(dmg, "stressTolerance"), FieldLine(dmg, "impactTolerance"), FieldLine(dmg, "repairable") });

                    sb.AppendLine("}");
                    sb.AppendLine();
                }

                return sb.ToString();
            }
            catch (Exception ex)
            {
                return "// Patch generation failed:\n// " + ex + "\n";
            }
        }

        private void AppendModuleBlock(System.Text.StringBuilder sb, string moduleName, IEnumerable<string> chunks)
        {
            var filtered = chunks.Where(c => !string.IsNullOrEmpty(c)).ToList();
            if (filtered.Count == 0) return;

            sb.AppendLine($"  @MODULE[{moduleName}]");
            sb.AppendLine("  {");

            foreach (var chunk in filtered)
            {
                // allow multi-line chunks (curves)
                var lines = chunk.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                foreach (var line in lines)
                {
                    if (line.Length == 0) sb.AppendLine();
                    else sb.AppendLine("    " + line);
                }
            }

            sb.AppendLine("  }");
            sb.AppendLine();
        }

        private AnimationCurve TryGetAnimationCurveFromFloatCurve(object floatCurveObj)
        {
            if (floatCurveObj == null) return null;

            // Common: FloatCurve.Curve (public) or FloatCurve.curve
            object acObj = SafeGet(floatCurveObj, "Curve") ?? SafeGet(floatCurveObj, "curve");
            if (acObj is AnimationCurve ac) return ac;

            // Fallback: find any AnimationCurve field inside FloatCurve
            var t = floatCurveObj.GetType();
            var f = t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                     .FirstOrDefault(x => typeof(AnimationCurve).IsAssignableFrom(x.FieldType));
            if (f != null)
            {
                try { return f.GetValue(floatCurveObj) as AnimationCurve; } catch { }
            }

            return null;
        }

        private string BuildFloatCurvePatchChunk(string curveName, object floatCurveObj)
        {
            var ac = TryGetAnimationCurveFromFloatCurve(floatCurveObj);
            if (ac == null) return null;

            // MM strategy:
            // 1) delete existing curve node
            // 2) recreate it with keys
            var sb = new System.Text.StringBuilder();

            sb.AppendLine($"!{curveName} {{}}");
            sb.AppendLine($"%{curveName}");
            sb.AppendLine("{");

            var keys = ac.keys ?? new Keyframe[0];
            for (int i = 0; i < keys.Length; i++)
            {
                var k = keys[i];
                // KSP FloatCurve config format:
                // key = time value inTangent outTangent
                sb.AppendLine($"  key = {k.time.ToString("0.######", CI)} {k.value.ToString("0.######", CI)} {k.inTangent.ToString("0.######", CI)} {k.outTangent.ToString("0.######", CI)}");
            }

            sb.AppendLine("}");

            return sb.ToString();
        }

        private string FieldLine(object module, string field)
        {
            object val = SafeGet(module, field);
            if (val == null) return null;

            if (val is float f) return $"@{field} = {f.ToString("0.###", CI)}";
            if (val is double d) return $"@{field} = {d.ToString("0.###", CI)}";
            if (val is int i) return $"@{field} = {i}";
            if (val is bool b) return $"@{field} = {(b ? "true" : "false")}";
            if (val.GetType().IsEnum) return $"@{field} = {val}";
            return $"@{field} = {val}";
        }

        private string GetPartInternalName(Part p)
        {
            if (p == null) return "";
            if (p.partInfo != null && !string.IsNullOrEmpty(p.partInfo.name))
                return p.partInfo.name;
            return p.name ?? "";
        }

        static ToolbarControl toolbarControl = null;
        internal const string MODID = "WheelTuner";
        internal const string MODNAME = "Wheel Tuner";
        private const string IconOnPath = "WheelTunerWindow/Icons/wheelTuner_on";
        private const string IconOffPath = "WheelTunerWindow/Icons/wheelTuner_off";


        // =========================
        // AppLauncher
        // =========================

        private void InitToolbar()
        {
            var scenes = ApplicationLauncher.AppScenes.FLIGHT |
                         ApplicationLauncher.AppScenes.VAB |
                         ApplicationLauncher.AppScenes.SPH;

            if (toolbarControl == null)
            {
                toolbarControl = gameObject.AddComponent<ToolbarControl>();
                toolbarControl.AddToAllToolbars(
                    onTrue: () => { _visible = true; },
                    onFalse: () => { _visible = false; },
                    scenes,
                    MODID,
                    "WheelTuner",
                    IconOnPath,
                    IconOffPath,
                    MODNAME);
            }


        }


        private Texture2D CreateSimpleIcon()
        {
            var tex = new Texture2D(38, 38, TextureFormat.ARGB32, false);
            for (int y = 0; y < tex.height; y++)
                for (int x = 0; x < tex.width; x++)
                    tex.SetPixel(x, y, new Color(0.15f, 0.15f, 0.15f, 1f));
            tex.Apply();
            tex.wrapMode = TextureWrapMode.Clamp;
            return tex;
        }

        // =========================
        // Expand state
        // =========================

        private bool GetExpanded(uint persistentId)
        {
            if (_expandedByPartId.TryGetValue(persistentId, out var ex)) return ex;
            _expandedByPartId[persistentId] = false;
            return false;
        }

        private void SetExpanded(uint persistentId, bool expanded) => _expandedByPartId[persistentId] = expanded;

        // =========================
        // Temp state
        // =========================

        private string GetTempString(object module, string key, string defaultValue)
        {
            int id = module != null ? module.GetHashCode() : 0;
            if (!_tempStrings.TryGetValue(id, out var d))
                _tempStrings[id] = d = new Dictionary<string, string>();

            if (!d.TryGetValue(key, out var v))
                d[key] = v = defaultValue;

            return v;
        }

        private void SetTempString(object module, string key, string value)
        {
            int id = module != null ? module.GetHashCode() : 0;
            if (!_tempStrings.TryGetValue(id, out var d))
                _tempStrings[id] = d = new Dictionary<string, string>();
            d[key] = value;
        }

        private float GetTempFloat(object module, string key, float defaultValue)
        {
            int id = module != null ? module.GetHashCode() : 0;
            if (!_tempFloats.TryGetValue(id, out var d))
                _tempFloats[id] = d = new Dictionary<string, float>();

            if (!d.TryGetValue(key, out var v))
                d[key] = v = defaultValue;

            return v;
        }

        private void SetTempFloat(object module, string key, float value)
        {
            int id = module != null ? module.GetHashCode() : 0;
            if (!_tempFloats.TryGetValue(id, out var d))
                _tempFloats[id] = d = new Dictionary<string, float>();
            d[key] = value;
        }

        // =========================
        // Reflection helpers
        // =========================

        private static object SafeGet(object obj, string name)
        {
            if (obj == null || string.IsNullOrEmpty(name)) return null;
            var t = obj.GetType();

            var p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p != null && p.CanRead)
            {
                try { return p.GetValue(obj, null); } catch { }
            }

            var f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null)
            {
                try { return f.GetValue(obj); } catch { }
            }

            return null;
        }

        private static bool SafeSet(object obj, string name, object value)
        {
            if (obj == null || string.IsNullOrEmpty(name)) return false;
            var t = obj.GetType();

            var p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p != null && p.CanWrite)
            {
                try
                {
                    object v = Coerce(value, p.PropertyType);
                    p.SetValue(obj, v, null);
                    return true;
                }
                catch { }
            }

            var f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null)
            {
                try
                {
                    object v = Coerce(value, f.FieldType);
                    f.SetValue(obj, v);
                    return true;
                }
                catch { }
            }

            return false;
        }

        private static object Coerce(object value, Type target)
        {
            if (value == null) return target.IsValueType ? Activator.CreateInstance(target) : null;
            if (target.IsAssignableFrom(value.GetType())) return value;

            try
            {
                if (target == typeof(float)) return Convert.ToSingle(value, CI);
                if (target == typeof(double)) return Convert.ToDouble(value, CI);
                if (target == typeof(int)) return Convert.ToInt32(value, CI);
                if (target == typeof(bool)) return Convert.ToBoolean(value, CI);
                if (target.IsEnum) return Enum.Parse(target, value.ToString(), true);
            }
            catch { }

            return value;
        }

        private static float SafeGetFloat(object obj, string name)
        {
            var o = SafeGet(obj, name);
            if (o == null) return 0f;
            try { return Convert.ToSingle(o, CI); } catch { return 0f; }
        }

        private static bool SafeGetBool(object obj, string name)
        {
            var o = SafeGet(obj, name);
            if (o == null) return false;
            try { return Convert.ToBoolean(o, CI); } catch { return false; }
        }

        private static void InvokeIfExists(object obj, string methodName, object[] args = null)
        {
            if (obj == null) return;
            var t = obj.GetType();
            var m = t.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (m == null) return;

            try
            {
                var parms = m.GetParameters();
                if ((args == null || args.Length == 0) && parms.Length == 0)
                    m.Invoke(obj, null);
                else if (args != null && parms.Length == args.Length)
                    m.Invoke(obj, args);
            }
            catch { }
        }
    }


}

