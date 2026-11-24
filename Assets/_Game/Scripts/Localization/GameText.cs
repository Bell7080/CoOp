using System.Collections.Generic;
using UnityEngine;

namespace CoOpHorror.Localization
{
    // Base language is Korean. English, Simplified Chinese and Japanese are planned.
    // Every player-facing string goes through a key here so call sites do not change
    // when the table later moves to com.unity.localization String Tables.
    public enum GameLanguage
    {
        Korean,             // ko-KR, base language
        English,            // en, planned
        ChineseSimplified,  // zh-Hans, planned
        Japanese            // ja, planned
    }

    public static class GameText
    {
        public static GameLanguage Current { get; set; } = GameLanguage.Korean;

        private static readonly Dictionary<string, string> Korean = new()
        {
            // Room panel
            ["room.title"] = "COOP / 네트워크 샌드박스",
            ["room.hint"] = "Esc: 커서 해제 후 이 패널 사용 · 게임 화면 클릭: 조작 복귀 · E: 동기화 신호",
            ["room.host"] = "인터넷 방 만들기",
            ["room.join"] = "참가",
            ["room.copy"] = "코드 복사",
            ["room.leave"] = "방 나가기",
            ["room.signal"] = "동기화 신호 보내기",
            ["room.codePlaceholder"] = "방 코드",
            ["room.codeLabel"] = "방 코드  {0}",
            ["room.codeEmpty"] = "방 코드  ---",
            ["room.roleHost"] = "방장 PC",
            ["room.roleClient"] = "참가자",
            ["room.summary"] = "v{0} | {1} | 인원 {2}/{3}",
            ["room.playerLine"] = "플레이어 {0}   체력 {1}   신호 {2}",
            ["room.ready"] = "준비",

            // Room status
            ["status.idle"] = "방을 만들거나 친구의 방 코드를 입력하세요.",
            ["status.creating"] = "인터넷 방을 만드는 중...",
            ["status.hosting"] = "이 PC가 방장입니다. 방 코드를 친구에게 알려주세요.",
            ["status.joining"] = "인터넷 방에 참가하는 중...",
            ["status.joined"] = "Relay를 통해 친구 PC에 연결되었습니다.",
            ["status.left"] = "연결을 끊었습니다. 다른 방을 만들거나 참가하세요.",
            ["status.closed"] = "연결이 종료되었습니다. 방장이 나갔을 수 있습니다. 다시 만들거나 참가하세요.",
            ["status.disconnectError"] = "연결 해제 오류: {0}",
            ["status.error"] = "오류: {0}",

            // Errors
            ["error.busy"] = "방 작업이 이미 진행 중입니다.",
            ["error.leaveFirst"] = "먼저 현재 방에서 나가세요.",
            ["error.enterCode"] = "방 코드를 입력하세요.",
            ["error.differentBuild"] = "게임 빌드가 다릅니다. 두 개발자가 같은 Git 커밋/빌드를 사용해야 합니다.",
            ["error.roomFull"] = "방이 가득 찼습니다(최대 {0}명).",

            // HUD
            ["hud.health"] = "체력 {0} / {1}",
            ["hud.controls"] = "WASD 이동 · Shift 달리기 · Space 점프 · 좌/우클릭 펀치 · Esc 커서",
            ["hud.dead"] = "쓰러졌습니다 — 잠시 후 부활합니다",
            ["hud.cursorFree"] = "커서 해제됨 — 게임 화면을 클릭하면 조작으로 돌아갑니다",
            ["hud.scoreLine"] = "플레이어 {0}{1}   체력 {2}",
            ["hud.you"] = " (나)",
        };

        // Planned languages fall back to Korean until their tables are filled in.
        private static readonly Dictionary<GameLanguage, Dictionary<string, string>> Tables = new()
        {
            [GameLanguage.Korean] = Korean,
        };

        public static string Get(string key)
        {
            if (Tables.TryGetValue(Current, out var table) && table.TryGetValue(key, out var value)) return value;
            if (Korean.TryGetValue(key, out var fallback)) return fallback;
            Debug.LogWarning($"Missing text key: {key}");
            return key;
        }

        public static string Format(string key, params object[] args) => string.Format(Get(key), args);

        private static Font uiFont;

        // Legacy uGUI font with OS fallbacks that cover Hangul (and later CJK languages).
        public static Font UiFont
        {
            get
            {
                if (uiFont != null) return uiFont;
                uiFont = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "맑은 고딕", "Microsoft YaHei", "Yu Gothic", "Arial" }, 16);
                if (uiFont == null) uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return uiFont;
            }
        }
    }
}
