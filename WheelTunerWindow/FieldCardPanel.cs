using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace WheelTunerWindow
{
    public partial class WheelTunerWindow
    {
        private GUIStyle _boldButton;

        private GUIStyle BoldButton()
        {
            if (_boldButton == null)
            {
                _boldButton = new GUIStyle(GUI.skin.button)
                {
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleLeft
                };
            }
            return _boldButton;
        }

        private void DrawFieldCardWindow(int id)
        {
            GUILayout.BeginVertical();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Quick fixes and baselines", BoldLabel());
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("X", GUILayout.Width(28)))
                _fieldCardVisible = false;
            GUILayout.EndHorizontal();

            GUILayout.Space(6);

            _fieldCardScroll = GUILayout.BeginScrollView(_fieldCardScroll);

            // Content (same as before, just in its own window)
            DrawFieldCardContent();

            GUILayout.EndScrollView();

            GUILayout.Space(6);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Copy to Clipboard", GUILayout.Width(140)))
                GUIUtility.systemCopyBuffer = BuildFieldCardText();

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
            GUI.DragWindow();
        }
        private string BuildFieldCardText()
        {
            return
        @"KSP Wheel Tuning — Field Card

Adjust → Fix
- Bounce / Oscillation → Increase damperRatio
- Bottoming Out → Increase suspensionDistance or springRatio
- Flips in Turns → Reduce steeringCurve (high-speed)
- Shimmy / Wobble → Increase damperRatio; reduce bogeyResponse
- Slides While Braking → Reduce maxBrakeTorque
- Gear Collapse → Increase springRatio + impactTolerance

Key Controls
- Suspension Distance → Ride height / travel
- Spring Ratio → Stiffness
- Damper Ratio → Bounce control
- Steering Curve → Speed-based steering
- Max Torque → Acceleration force
- Bogey Angle / Response → Load sharing
- Wheel Lock → Ground anchoring (only when stopped)

Quick Presets
- Light Rover: spring 5–7 | damper 1–1.5 | low torque
- Heavy Rover: spring 10–15 | damper 2–3 | bogey ON
- Main Gear: spring 12–18 | damper 3–4
- Nose Gear: spring 8–12 | reduced steering
- Hard Landing: spring 18–25 | damper 4+ | high impact tol

Tuning Order
1) Suspension (spring + damper)
2) Steering stability
3) Motor torque / curves
4) Bogey (if present)
5) Lock only when stopped
Rebuild wheels after major changes";
        }


        private void DrawFieldCardContent()
        {
            DrawFieldCardSection("Adjust → Fix");
            DrawFieldCardLine("Bounce / Oscillation", "Increase damperRatio");
            DrawFieldCardLine("Bottoming Out", "Increase suspensionDistance or springRatio");
            DrawFieldCardLine("Flips in Turns", "Reduce steeringCurve (high-speed)");
            DrawFieldCardLine("Shimmy / Wobble", "Increase damperRatio; reduce bogeyResponse");
            DrawFieldCardLine("Slides While Braking", "Reduce maxBrakeTorque");
            DrawFieldCardLine("Gear Collapse", "Increase springRatio + impactTolerance");

            GUILayout.Space(6);

            DrawFieldCardSection("Key Controls");
            DrawFieldCardLine("Suspension Distance", "Ride height / travel");
            DrawFieldCardLine("Spring Ratio", "Stiffness");
            DrawFieldCardLine("Damper Ratio", "Bounce control");
            DrawFieldCardLine("Steering Curve", "Speed-based steering");
            DrawFieldCardLine("Max Torque", "Acceleration force");
            DrawFieldCardLine("Bogey Angle / Response", "Load sharing");
            DrawFieldCardLine("Wheel Lock", "Ground anchoring (only when stopped)");

            GUILayout.Space(6);

            DrawFieldCardSection("Quick Presets");
            DrawFieldCardText("Light Rover (Mun/Minmus): spring 5–7 | damper 1–1.5 | low torque");
            DrawFieldCardText("Heavy Rover (Duna/Eve): spring 10–15 | damper 2–3 | bogey ON");
            DrawFieldCardText("Aircraft Main Gear: spring 12–18 | damper 3–4");
            DrawFieldCardText("Aircraft Nose Gear: spring 8–12 | reduced steering");
            DrawFieldCardText("Hard Landing / SSTO: spring 18–25 | damper 4+ | high impact tol");

            GUILayout.Space(6);

            DrawFieldCardSection("Tuning Order");
            DrawFieldCardText("1) Suspension (spring + damper)");
            DrawFieldCardText("2) Steering stability");
            DrawFieldCardText("3) Motor torque / curves");
            DrawFieldCardText("4) Bogey (if present)");
            DrawFieldCardText("5) Lock only when stopped");
            DrawFieldCardText("Rebuild wheels after major changes");
        }
        private void DrawFieldCardSection(string title)
        {
            GUILayout.Label(title, BoldLabel());
        }

        private void DrawFieldCardLine(string left, string right)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(left, GUILayout.Width(180));
            GUILayout.Label("→", GUILayout.Width(18));
            GUILayout.Label(right);
            GUILayout.EndHorizontal();
        }

        private void DrawFieldCardText(string text)
        {
            GUILayout.Label(text);
        }

    }
}
