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
            ["room.hint"] = "Esc: 커서 해제 후 이 패널 사용 · 게임 화면 클릭: 조작 복귀 · G: 동기화 신호",
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
            ["hud.controls"] = "WASD 이동 · Shift 달리기 · Space 점프(스킬체크) · 좌클릭 상호작용 · Q 내려놓기 · F 손전등 · Esc 커서",
            ["hud.dead"] = "쓰러졌습니다 — 잠시 후 부활합니다",
            ["hud.discharged"] = "방전되었습니다 — 동료가 몸을 때려 전류를 채우면 되살아납니다",
            ["hud.cursorFree"] = "커서 해제됨 — 게임 화면을 클릭하면 조작으로 돌아갑니다",
            ["hud.scoreLine"] = "플레이어 {0}{1}   체력 {2}",
            ["hud.you"] = " (나)",

            // ElecMole core-loop prototype
            ["proto.power"] = "전지 {0}%",
            ["proto.flashlightOn"] = "손전등 켜짐 [F]",
            ["proto.flashlightOff"] = "손전등 꺼짐 [F]",
            ["proto.hint"] = "발전기 → 공용 전기 → 가전 충전 → 송신구 정산 → 암전 → 우주선 탈출",
            ["proto.objectiveDoor"] = "발전기를 돌려 공용 전기를 모으고, 우주선 문에 전력을 넣어 여세요",
            ["proto.objectiveTransmitter"] = "맵 깊숙한 곳의 송신구를 찾아 전력을 넣으세요",
            ["proto.objective"] = "충전한 가전제품을 송신구에 꽂으세요  {0} / {1}",
            ["proto.settling"] = "정산 중…",
            ["proto.settlingBanner"] = "송신구가 가전제품을 빨아들이고 있다! 날아오는 물건 조심",
            ["proto.blackout"] = "암전! 우주선으로 돌아가세요",
            ["proto.objectiveReturn"] = "어둠 속에서 괴물을 피해 우주선으로 돌아가세요",
            ["proto.objectiveLaunch"] = "출발 버튼을 누르세요",
            ["proto.launching"] = "출발까지 {0}초 — 모두 우주선 안으로!",
            ["proto.escaped"] = "탈출 성공!",
            ["proto.leftBehind"] = "우주선이 떠났습니다… 전류가 바닥나고 있다",
            ["proto.defeat"] = "전원 방전 — 패배",
            ["proto.reviveProgress"] = "재세동 {0}% — 동료가 때려 주기를 기다리는 중",
            ["proto.skillPress"] = "Space!",
            ["proto.skillGreat"] = "완벽!",
            ["proto.skillGood"] = "좋음",
            ["proto.skillMiss"] = "실패! 펑!",
            ["proto.monsterNear"] = "무언가 쫓아오고 있다!",

            // Interaction prompts
            ["interact.generator"] = "[좌클릭 길게] 발전기 돌리기 · 스킬체크는 Space",
            ["interact.generatorStalled"] = "발전기가 터졌다! 잠시 후 다시 돌아갑니다",
            ["interact.generatorFull"] = "발전기 — 공용 전기가 가득 찼습니다",
            ["interact.charge"] = "[좌클릭 길게] {0} 충전하기 ({1}%)",
            ["interact.chargeStalled"] = "{0} 과부하! 잠시 멈춤 ({1}%)",
            ["interact.noPower"] = "{0} 충전 {1}% — 공용 전기가 없습니다. 발전기를 돌리세요",
            ["interact.pickUp"] = "[좌클릭] {0} 들기 · 예상 금액 {1}",
            ["interact.handsFull"] = "이미 다른 물건을 들고 있습니다",
            ["interact.plugged"] = "{0} — 송신구에 연결됨",
            ["interact.plug"] = "[좌클릭] {0} 송신구에 꽂기",
            ["interact.transmitterStatus"] = "송신구 {0} / {1} — 충전한 가전제품을 들고 와서 꽂으세요",
            ["interact.transmitterCharge"] = "[좌클릭 길게] 송신구에 전력 넣기 ({0}%)",
            ["interact.transmitterNoPower"] = "송신구 전력 {0}% — 공용 전기가 없습니다",
            ["interact.transmitterStalled"] = "송신구 과부하! 잠시 멈춤 ({0}%)",
            ["interact.transmitterDone"] = "송신구 — 정산 완료",
            ["interact.carrying"] = "{0} 들고 있음 · [Q] 내려놓기",
            ["interact.doorCharge"] = "[좌클릭 길게] 문에 전력 넣기 ({0}%)",
            ["interact.doorNoPower"] = "닫힌 문 ({0}%) — 공용 전기가 없습니다. 발전기를 돌리세요",
            ["interact.launch"] = "[좌클릭] 출발 버튼 누르기",
            ["interact.launching"] = "출발까지 {0}초",
            ["interact.launchLocked"] = "출발 버튼 — 송신을 먼저 끝내야 합니다",
            ["interact.revive"] = "[좌클릭 연타] 플레이어 {0} 재세동 ({1}%)",
            ["interact.cctvPanel"] = "[좌클릭] CCTV 보기 (연결 {0} / {1})",
            ["interact.cctvCharge"] = "[좌클릭 길게] {0} 켜기 ({1}%)",
            ["interact.cctvNoPower"] = "{0} 전력 {1}% — 공용 전기가 없습니다",
            ["interact.cctvOnline"] = "{0} — 우주선 CCTV 패널에 연결됨",

            // CCTV view
            ["cctv.controls"] = "마우스: 카메라 돌리기 · 휠: 확대 · A/D 또는 숫자: 카메라 전환 · 우클릭/Q/Tab: 나가기",
            ["cctv.noSignal"] = "신호 없음 — 밖에서 이 카메라에 전력을 넣어야 합니다",
            ["cctv.rec"] = "● REC",
            ["cctv.zoom"] = "확대 ×{0}",
            ["cctv.cam1"] = "CAM 01 · 서쪽 창고",
            ["cctv.cam2"] = "CAM 02 · 중앙 홀",
            ["cctv.cam3"] = "CAM 03 · 우주선 앞 통로",
            ["cctv.cam4"] = "CAM 04 · 동쪽 창고",
            ["cctv.cam5"] = "CAM 05 · 송신구 구역",
            ["cctv.cam6"] = "CAM 06 · 북쪽 복도",

            // Appliances (placeholder set)
            ["appliance.tv"] = "TV",
            ["appliance.microwave"] = "전자레인지",
            ["appliance.radio"] = "라디오",
            ["appliance.fan"] = "선풍기",
            ["appliance.toaster"] = "토스터",
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
