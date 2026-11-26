using System.Collections.Generic;
using CoOpHorror.Gameplay;
using UnityEngine;

namespace CoOpHorror.Editor
{
    // Item support: a supply terminal in the ship (buy items with watts from your energy tank) and the supply station against the
    // dorm's west wall, where the people outside pick them up.
    public static partial class ElecMolePrototypeSetup
    {
        private static readonly Vector3 SupplyStationPosition = new(-35.4f, 0f, -1f);
        private const float SupplyStationYaw = 90f; // faces east, into the dorm
        private static readonly Vector3 SupplyKioskPosition = new(-25.55f, 0f, -18.6f); // ship, west wall
        private const float SupplyKioskYaw = 90f;

        private static void BuildSupply()
        {
            BuildSupplyStation(SupplyStationPosition, SupplyStationYaw);
            BuildSupplyKiosk(SupplyKioskPosition, SupplyKioskYaw);
        }

        private static SupplyStation BuildSupplyStation(Vector3 position, float yaw)
        {
            var root = NetRoot("SupplyStation", position, yaw);
            var body = Mat("SupplyBody", new Color(0.62f, 0.5f, 0.16f), tex: Texture("Metal"), tiling: 1f);
            var dark = Mat("GeneratorDark", new Color(0.1f, 0.1f, 0.1f));
            var hazard = Mat("Hazard", Color.white, tex: Texture("Hazard"), tiling: 4f);
            var lampMat = Mat("Indicator", Color.white, unlit: true);
            var t = root.transform;
            const float w = 1.3f, h = 1.9f, d = 0.8f;
            // shell with a dark recess (the hatch opening) at chest height; local +Z is the front
            Box(t, "Back", new Vector3(0f, h / 2f, -d / 2f + 0.05f), new Vector3(w, h, 0.1f), body);
            Box(t, "SideL", new Vector3(-w / 2f + 0.05f, h / 2f, 0f), new Vector3(0.1f, h, d), body);
            Box(t, "SideR", new Vector3(w / 2f - 0.05f, h / 2f, 0f), new Vector3(0.1f, h, d), body);
            Box(t, "Base", new Vector3(0f, 0.45f, 0f), new Vector3(w, 0.9f, d), body);
            Box(t, "Top", new Vector3(0f, h - 0.25f, 0f), new Vector3(w, 0.5f, d), body);
            Shape(t, "Recess", PrimitiveType.Cube, new Vector3(0f, 1.17f, -0.2f), Vector3.zero, new Vector3(w - 0.2f, 0.55f, 0.02f), dark);
            Shape(t, "BaseStripe", PrimitiveType.Cube, new Vector3(0f, 0.12f, d / 2f + 0.005f), Vector3.zero, new Vector3(w, 0.2f, 0.01f), hazard);
            Shape(t, "Sign", PrimitiveType.Cube, new Vector3(0f, h - 0.25f, d / 2f + 0.005f), Vector3.zero, new Vector3(0.7f, 0.2f, 0.01f), Mat("SupplySign", new Color(0.9f, 0.9f, 0.85f)));
            // a red cross and a bolt on the sign so it reads at a glance
            Shape(t, "SignCross", PrimitiveType.Cube, new Vector3(-0.18f, h - 0.25f, d / 2f + 0.012f), Vector3.zero, new Vector3(0.14f, 0.04f, 0.01f), Mat("Blood", new Color(0.7f, 0.05f, 0.05f)));
            Shape(t, "SignCross2", PrimitiveType.Cube, new Vector3(-0.18f, h - 0.25f, d / 2f + 0.012f), Vector3.zero, new Vector3(0.04f, 0.14f, 0.01f), Mat("Blood", new Color(0.7f, 0.05f, 0.05f)));
            Shape(t, "SignArrow", PrimitiveType.Cube, new Vector3(0.18f, h - 0.25f, d / 2f + 0.012f), new Vector3(0f, 0f, 45f), new Vector3(0.1f, 0.1f, 0.01f), Mat("AdrenalineGreen", new Color(0.2f, 0.95f, 0.4f), unlit: true));

            // hatch hinged on its top edge
            var hatch = new GameObject("Hatch").transform;
            hatch.SetParent(t, false);
            hatch.localPosition = new Vector3(0f, 1.45f, d / 2f);
            Box(hatch, "Plate", new Vector3(0f, -0.28f, 0f), new Vector3(w - 0.18f, 0.56f, 0.05f), body);
            Shape(hatch, "Handle", PrimitiveType.Cube, new Vector3(0f, -0.5f, 0.04f), Vector3.zero, new Vector3(0.3f, 0.03f, 0.04f), dark);

            // tray with the pack on it
            var tray = new GameObject("Tray").transform;
            tray.SetParent(t, false);
            tray.localPosition = new Vector3(0f, 0.92f, 0f);
            Box(tray, "Shelf", Vector3.zero, new Vector3(w - 0.25f, 0.04f, 0.6f), dark);
            var medkit = Group(tray, "Medkit", new Vector3(0f, 0.02f, 0.05f), 0f).gameObject;
            Shape(medkit.transform, "Case", PrimitiveType.Cube, new Vector3(0f, 0.11f, 0f), Vector3.zero, new Vector3(0.42f, 0.22f, 0.3f), Mat("MedkitWhite", new Color(0.88f, 0.88f, 0.84f)));
            Shape(medkit.transform, "Cross", PrimitiveType.Cube, new Vector3(0f, 0.11f, 0.151f), Vector3.zero, new Vector3(0.18f, 0.05f, 0.01f), Mat("Blood", new Color(0.7f, 0.05f, 0.05f)));
            Shape(medkit.transform, "Cross2", PrimitiveType.Cube, new Vector3(0f, 0.11f, 0.151f), Vector3.zero, new Vector3(0.05f, 0.18f, 0.01f), Mat("Blood", new Color(0.7f, 0.05f, 0.05f)));
            var battery = Group(tray, "Adrenaline", new Vector3(0f, 0.02f, 0.05f), 0f).gameObject;
            Shape(battery.transform, "Case", PrimitiveType.Cube, new Vector3(0f, 0.04f, 0f), Vector3.zero, new Vector3(0.36f, 0.08f, 0.16f), Mat("MedkitWhite", new Color(0.88f, 0.88f, 0.84f)));
            Shape(battery.transform, "Barrel", PrimitiveType.Cylinder, new Vector3(0f, 0.12f, 0f), new Vector3(0f, 0f, 90f), new Vector3(0.06f, 0.12f, 0.06f), Mat("AdrenalineGreen", new Color(0.2f, 0.95f, 0.4f), unlit: true));
            Shape(battery.transform, "Plunger", PrimitiveType.Cylinder, new Vector3(-0.15f, 0.12f, 0f), new Vector3(0f, 0f, 90f), new Vector3(0.025f, 0.05f, 0.025f), Mat("ApplianceMetal", new Color(0.6f, 0.62f, 0.66f)));
            Shape(battery.transform, "Needle", PrimitiveType.Cylinder, new Vector3(0.15f, 0.12f, 0f), new Vector3(0f, 0f, 90f), new Vector3(0.008f, 0.04f, 0.008f), Mat("ApplianceMetal", new Color(0.6f, 0.62f, 0.66f)));
            foreach (var c in medkit.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            foreach (var c in battery.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            medkit.SetActive(false);
            battery.SetActive(false);

            var lamp = Shape(t, "StatusLamp", PrimitiveType.Cube, new Vector3(0.45f, 1.6f, d / 2f + 0.01f), Vector3.zero, new Vector3(0.12f, 0.06f, 0.02f), lampMat);
            Shape(t, "BeaconDome", PrimitiveType.Sphere, new Vector3(0f, h + 0.08f, 0f), Vector3.zero, new Vector3(0.22f, 0.16f, 0.22f), Mat("BeaconAmber", new Color(1f, 0.6f, 0.1f), unlit: true));
            var beaconPivot = new GameObject("BeaconSpin").transform;
            beaconPivot.SetParent(t, false);
            beaconPivot.localPosition = new Vector3(0f, h + 0.1f, 0f);
            var beacon = new GameObject("Beacon").AddComponent<Light>();
            beacon.transform.SetParent(beaconPivot, false);
            beacon.transform.localRotation = Quaternion.Euler(10f, 0f, 0f);
            beacon.type = LightType.Spot;
            beacon.spotAngle = 70f;
            beacon.range = 9f;
            beacon.intensity = 4f;
            beacon.color = new Color(1f, 0.6f, 0.15f);
            beacon.enabled = false;
            Obstacle(root, new Vector3(0f, h / 2f, 0f), new Vector3(w, h, d));

            var station = root.AddComponent<SupplyStation>();
            station.Hatch = hatch;
            station.Tray = tray;
            station.MedkitModel = medkit;
            station.AdrenalineModel = battery;
            station.Lamp = lamp.GetComponent<Renderer>();
            station.Beacon = beacon;
            return station;
        }

        // The supply terminal in the ship: a standing kiosk with a big touch screen (the shop). Aim at a row,
        // click, confirm on the screen; the team's money pays and the item goes to the supply station.
        private static void BuildSupplyKiosk(Vector3 position, float yaw)
        {
            var root = NetRoot("SupplyTerminal", position, yaw);
            var t = root.transform;
            var body = Mat("SupplyBody", new Color(0.62f, 0.5f, 0.16f), tex: Texture("Metal"), tiling: 1f);
            var dark = Mat("GeneratorDark", new Color(0.1f, 0.1f, 0.1f));
            // local +Z is the front, facing the player
            Box(t, "Pedestal", new Vector3(0f, 0.5f, -0.05f), new Vector3(0.7f, 1f, 0.4f), body);
            Box(t, "Head", new Vector3(0f, 1.42f, -0.1f), new Vector3(0.8f, 0.84f, 0.3f), body);
            Shape(t, "Bezel", PrimitiveType.Cube, new Vector3(0f, 1.42f, 0.055f), Vector3.zero, new Vector3(0.74f, 0.58f, 0.02f), dark);
            Shape(t, "Screen", PrimitiveType.Cube, new Vector3(0f, 1.42f, 0.066f), Vector3.zero, new Vector3(0.66f, 0.5f, 0.005f), Mat("KioskScreen", new Color(0.01f, 0.04f, 0.02f), unlit: true));
            Shape(t, "Stripe", PrimitiveType.Cube, new Vector3(0f, 0.12f, 0.155f), Vector3.zero, new Vector3(0.7f, 0.16f, 0.01f), Mat("Hazard", Color.white, tex: Texture("Hazard"), tiling: 4f));
            PointLight(t, "ScreenGlow", new Vector3(0f, 1.4f, 0.45f), new Color(0.45f, 1f, 0.55f), 0.35f, 2.2f);
            Obstacle(root, new Vector3(0f, 0.9f, -0.05f), new Vector3(0.8f, 1.8f, 0.45f));
            // the touch surface: a thin box just in front of the glass
            var hit = new GameObject("ScreenHit").AddComponent<BoxCollider>();
            hit.transform.SetParent(t, false);
            hit.transform.localPosition = new Vector3(0f, 1.42f, 0.075f);
            hit.size = new Vector3(0.66f, 0.5f, 0.01f);
            // the UI faces its -Z: turn it towards the player, a hair in front of the glass
            var ui = new GameObject("ScreenUI").transform;
            ui.SetParent(t, false);
            ui.localPosition = new Vector3(0f, 1.42f, 0.07f);
            ui.localRotation = Quaternion.Euler(0f, 180f, 0f);
            var terminal = root.AddComponent<SupplyTerminal>();
            terminal.ScreenHit = hit;
            terminal.ScreenRoot = ui;
            terminal.ScreenSize = new Vector2(0.64f, 0.48f);
        }
    }
}
