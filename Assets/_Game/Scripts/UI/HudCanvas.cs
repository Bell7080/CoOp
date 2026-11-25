using UnityEngine;

namespace CoOpHorror.UI
{
    // Low-quality HUD: game canvases are drawn by the main camera (Screen Space - Camera) so the low render
    // scale pixelates them together with the scene. Falls back to an overlay when there is no camera.
    public static class HudCanvas
    {
        public const float PlaneDistance = 0.3f;

        // Drawing the HUD at the low render scale made text unreadable, so it stays a sharp overlay for now.
        public static bool PixelatedHud = false;

        public static void Follow(Canvas canvas)
        {
            if (canvas == null) return;
            if (!PixelatedHud)
            {
                if (canvas.renderMode != RenderMode.ScreenSpaceOverlay) canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                return;
            }
            var cam = Camera.main;
            if (cam == null)
            {
                if (canvas.renderMode != RenderMode.ScreenSpaceOverlay) canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                return;
            }
            if (canvas.renderMode == RenderMode.ScreenSpaceCamera && canvas.worldCamera == cam) return;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = PlaneDistance;
        }
    }
}
