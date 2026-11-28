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
            ["hud.healthLabel"] = "HP",
            ["hud.controls"] = "WASD 이동 · Shift 달리기 · Ctrl 웅크리기(숨기) · Space 점프 · 좌클릭 코 뻗기(꾹: 전력 흡수) · 우클릭 꾹: 전력 주입(송신기·가전·전등·CCTV) · V 무전 · F 손전등 · F1 개발 도구",
            ["hud.discharged"] = "방전되었습니다 — 유령이 되어 동료를 관전합니다",
            ["hud.cursorFree"] = "커서 해제됨 — 게임 화면을 클릭하면 조작으로 돌아갑니다",
            ["hud.scoreLine"] = "플레이어 {0}{1}   체력 {2}",
            ["hud.you"] = " (나)",

            // ElecMole core-loop prototype
            ["proto.flashlightOn"] = "손전등 켜짐 [F]",
            ["proto.flashlightOff"] = "손전등 꺼짐 [F]",
            ["proto.hint"] = "가전 전력을 코로 흡수 → 우주선 송신기에 주입 → 필요 전력을 채우면 송신 → 남는 전력은 돈으로 환전 → 암전 → 우주선 탈출",
            ["proto.objectiveDoor"] = "우주선 문 옆 버튼을 눌러 문을 여세요",
            ["proto.objectiveTransmitter"] = "가전제품의 전력을 코로 빨아 우주선 송신기에 넣으세요",
            ["proto.objective"] = "가전의 전력을 코로 빨아 우주선 송신기에 주입하세요  {0} / {1}W",
            ["proto.settling"] = "송신 중…",
            ["proto.settlingBanner"] = "송신기가 전력을 쏘아 올린다!",
            ["proto.blackout"] = "암전! 우주선으로 돌아가세요",
            ["proto.objectiveReturn"] = "어둠 속에서 괴물을 피해 우주선으로 돌아가세요",
            ["proto.objectiveLaunch"] = "출발 버튼을 누르세요",
            ["proto.launching"] = "출발까지 {0}초 — 모두 우주선 안으로!",
            ["proto.escaped"] = "탈출 성공! 정산 {0}W",
            ["proto.leftBehind"] = "우주선이 떠났습니다… 전류가 바닥나고 있다",
            ["proto.defeat"] = "전원 방전 — 패배",
            ["proto.reviveProgress"] = "재세동 {0}% — 동료가 시체를 때리거나 우주선으로 옮겨야 합니다",
            ["proto.spectating"] = "유령 관전 중: 플레이어 {0} · 좌/우클릭: 전환",
            ["proto.spectatingNobody"] = "유령 관전 중: 살아 있는 동료가 없습니다",
            ["proto.radioWalkie"] = "무전 송신 중 → 우주선 스피커",
            ["proto.radioMic"] = "우주선 방송 중 → 모든 플레이어",
            ["proto.radioHearWalkie"] = "무전 수신: 플레이어 {0}",
            ["proto.radioHearMic"] = "우주선 방송: 플레이어 {0}",
            ["proto.skillPress"] = "Space!",
            ["proto.skillGreat"] = "완벽!",
            ["proto.skillGood"] = "좋음",
            ["proto.skillMiss"] = "실패! 펑!",
            ["proto.monsterNear"] = "무언가 쫓아오고 있다!",

            // Interaction prompts
            ["interact.handsFull"] = "이미 다른 물건을 들고 있습니다",
            ["interact.carrying"] = "{0} 염력으로 드는 중 ({1}/{2}명) · 우클릭 꾹+마우스: 돌리기 · 좌클릭 떼면 놓기",
            ["interact.grab"] = "[좌클릭 꾹] {0} 염력으로 들기",
            ["interact.grabHeavy"] = "[좌클릭 꾹] {0} 같이 들기 ({1}/{2}명 — 모자라면 끌기만 됨)",
            ["cctv.radarTitle"] = "전파 탐지 · 실시간 지도",
            ["cctv.radarLegend"] = "연결된 구역 · 녹색: 동료 / 붉은색: 위험",
            ["item.value"] = "{0}W",
            ["item.loss"] = "-{0}W",
            ["interact.doorOpen"] = "[좌클릭] 문 열기",
            ["interact.dragOpen"] = "[좌클릭 꾹] 코로 {0} 잡기 · 화면을 끌어서 열기",
            ["interact.dragClose"] = "[좌클릭 꾹] 코로 {0} 잡기 · 화면을 끌어서 닫기",
            ["interact.dragHolding"] = "{0} 잡는 중 · 시선을 돌려 당기거나 밀기 · 떼면 관성으로 계속 움직임",
            ["openable.door"] = "문",
            ["openable.wardrobe"] = "장롱 문",
            ["openable.cupboard"] = "찬장 문",
            ["openable.locker"] = "사물함 문",
            ["openable.drawer"] = "서랍",
            ["cctv.pageMinimap"] = "미니맵 >",
            ["cctv.pageFeed"] = "< 카메라",
            ["cctv.minimapTitle"] = "미니맵 — 켜진 카메라가 있는 방만 보입니다 (초록: 아군, 빨강: 괴물)",
            ["interact.doorClose"] = "[좌클릭] 문 닫기",
            ["interact.doorBlocked"] = "문 사이에 누가 있어 닫을 수 없습니다",
            ["interact.launch"] = "[좌클릭] 출발! (송신기가 전력을 쏘아 올리고 암전됩니다)",
            ["interact.launching"] = "출발까지 {0}초",
            ["interact.launchLocked"] = "출발 버튼 — 송신기를 채워야 합니다 ({0} / {1}W)",
            ["supply.title"] = "지원 물품 주문",
            ["supply.medkit"] = "구급키트",
            ["supply.adrenaline"] = "아드레날린",
            ["supply.sending"] = "지원스테이션으로 전송 중",
            ["supply.waiting"] = "스테이션 도착 · 수령 대기",
            ["hover.supplyIdle"] = "지원스테이션 — 우주선 단말기에서 주문한 물품이 여기 도착",
            ["interact.suck"] = "[좌클릭 꾹] {0} 전력 흡수 ({1}W)",
            ["interact.drained"] = "{0} — 방전됨",
            ["interact.tankEmpty"] = "에너지 통이 비었습니다 — 가전의 전력을 흡수해 오세요",
            ["interact.inject"] = "[우클릭 꾹] 송신기에 전력 주입 (통 {0}W)",
            ["hover.watts"] = "{0}W 남음 / {1}W",
            ["hover.drained"] = "방전됨",
            ["hud.tankLabel"] = "에너지 통 (W)",
            ["transmitter.name"] = "최종 전력 송신기",
            ["transmitter.progress"] = "충전 {0} / {1}W",
            ["transmitter.firing"] = "송신 중!",
            ["transmitter.done"] = "송신 완료 · 남는 전력 환전",
            ["settle.stored"] = "충전됨  {0}W",
            ["settle.charging"] = "우클릭으로 코를 대고 전력을 주입하세요",
            ["interact.exchange"] = "[우클릭 꾹] 남는 전력 환전 (통 {0}W)",
            ["interact.giveBack"] = "[우클릭 꾹] {0}에 전력 되돌리기 ({1} / {2}W)",
            ["transmitter.exchangeRate"] = "목표 달성 — 출발 가능 · 더 넣는 전력은 {0}W당 $1로 환전",
            ["settle.exchange"] = "목표 달성 · 출발 가능 · 남는 전력 환전 (팀 돈 ${0})",
            ["hud.money"] = "팀 돈 ${0}",
            ["item.crank"] = "발전기 손잡이",
            ["item.crankInfo"] = "우주선 발전기에서 떨어져 나온 손잡이. 어딘가에 쓸 데가 있을지도…",
            ["dev.restart"] = "빠른 재시작",
            ["interact.drainTransmitter"] = "[좌클릭 꾹] 송신기 전력 빼내기 ({0}W)",
            ["settle.ready"] = "가득 참 · 출발 버튼으로 출발",
            ["proto.blackoutRun"] = "암전! {0}초 뒤 출발 — 우주선으로!",
            ["hover.medkitEffect"] = "체력 +40 회복",
            ["hover.adrenalineEffect"] = "20초 동안 달려도 지치지 않음",
            ["shop.title"] = "지원 물품 상점",
            ["shop.credits"] = "보유 ${0}",
            ["shop.confirm"] = "구매하시겠습니까?",
            ["shop.ok"] = "확인",
            ["shop.cancel"] = "취소",
            ["shop.ordering"] = "주문 중...",
            ["shop.bought"] = "{0} 구매 완료\n지원스테이션으로 전송 중",
            ["shop.noMoney"] = "돈이 부족합니다 — 송신기에 남는 전력을 넣어 환전하세요",
            ["shop.busy"] = "배송 중인 물품이 있습니다\n스테이션에서 먼저 받아 가세요",
            ["shop.tooFar"] = "단말기에서 너무 멉니다",
            ["shop.hint"] = "항목을 눌러 구매 (팀 돈에서 차감)",
            ["settle.target"] = "필요 전력  {0}W",
            ["shop.soldOut"] = "품절입니다",
            ["shop.tabMedical"] = "의료품",
            ["interact.corpse"] = "[좌클릭 연타] 플레이어 {0} 재세동 ({1}%) · [좌클릭 꾹] 시체 옮기기",
            ["interact.reviveOnly"] = "[좌클릭 연타] 플레이어 {0} 재세동 ({1}%)",
            ["interact.reviveHold"] = "[E 길게] 체력 {1}을 넣어 플레이어 {0} 부활 ({2}%)",
            ["interact.reviveWeak"] = "체력이 {0}보다 많아야 부활시킬 수 있습니다",
            ["interact.corpseCarry"] = "[좌클릭 꾹] 시체 옮기기",
            ["interact.shipMic"] = "우주선 방송 마이크 — [V 꾹] 모든 플레이어에게 방송",
            ["interact.cctvPanel"] = "[좌클릭] CCTV 보기 (연결 {0} / {1})",

            // CCTV view
            ["cctv.controls"] = "화면 끌기: 카메라 돌리기 · 휠: 확대 · 지도 클릭/A·D/숫자: 카메라 전환 · Q·Tab·Esc·우클릭: 나가기",
            ["cctv.noSignal"] = "신호 없음 — 밖에서 이 카메라에 전력을 넣어야 합니다",
            ["cctv.rec"] = "● REC",
            ["cctv.zoom"] = "확대 ×{0}",
            ["cctv.mapTitle"] = "CCTV 지도",
            ["cctv.mapShip"] = "우주선 (나)",
            ["corpse.name"] = "플레이어 {0}의 시체",
            ["lamp.ceiling"] = "천장 전등",
            ["appliance.kettle"] = "커피포트",
            ["appliance.ricecooker"] = "전기밥솥",
            ["cctv.cam1"] = "CAM 01 · 남쪽 창고",
            ["cctv.cam2"] = "CAM 02 · 숙소",
            ["cctv.cam3"] = "CAM 03 · 사무실",
            ["cctv.cam4"] = "CAM 04 · 휴게실",
            ["cctv.cam5"] = "CAM 05 · 동쪽 창고",
            ["cctv.cam6"] = "CAM 06 · 서버실",
            ["cctv.cam7"] = "CAM 07 · 송신구실",
            ["cctv.cam8"] = "CAM 08 · 보일러실",
            ["dev.title"] = "개발 도구 (F1)",
            ["dev.status"] = "역할 {0} · 인원 {1} · 괴물 정지 {2}",
            ["dev.offline"] = "오프라인",
            ["dev.host"] = "방장",
            ["dev.client"] = "참가자",
            ["dev.on"] = "켜짐",
            ["dev.off"] = "꺼짐",
            ["dev.editorOnly"] = "에디터에서만 됩니다",
            ["dev.player2"] = "Player 2 같이 켜기 (재시작)",
            ["dev.freeze"] = "괴물 정지/재개",
            ["dev.charge"] = "내 에너지 통 가득",
            ["dev.stageUp"] = "스테이지 +1 (흡수 속도 증가)",
            ["dev.toTransmitter"] = "송신기로 이동",
            ["dev.toShip"] = "우주선으로 이동",
            ["dev.close"] = "닫기",

            // Appliances (placeholder set)
            ["appliance.tv"] = "TV",
            ["appliance.microwave"] = "전자레인지",
            ["appliance.radio"] = "라디오",
            ["appliance.fan"] = "선풍기",
            ["appliance.toaster"] = "토스터",

            // Creatures (drained while stunned, torn off a back)
            ["creature.tv"] = "TV 괴물",
            ["creature.fan"] = "선풍기 괴물",
            ["creature.clock"] = "자명종 거미",
            ["creature.generic"] = "괴물",
            ["item.battery"] = "배터리",
            ["interact.pullOff"] = "[좌클릭 꾹] {0} 떼어내기",
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

        // Fonts (SIL OFL 1.1, Resources/Fonts): Fusion Pixel 12px for text, one file per script so Chinese and
        // Japanese glyph shapes are right; DSEG7 Classic (seven-segment) for plain numbers like 47% or 03:12.
        private static Font uiFont;
        private static GameLanguage uiFontLanguage;
        private static Font numberFont;

        public static Font UiFont
        {
            get
            {
                if (uiFont != null && uiFontLanguage == Current) return uiFont;
                var file = Current switch
                {
                    GameLanguage.Japanese => "fusion-pixel-12px-proportional-ja",
                    GameLanguage.ChineseSimplified => "fusion-pixel-12px-proportional-zh_hans",
                    _ => "fusion-pixel-12px-proportional-ko", // Korean + Latin
                };
                uiFont = Resources.Load<Font>("Fonts/FusionPixel/" + file);
                if (uiFont == null) uiFont = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "Microsoft YaHei", "Yu Gothic", "Arial" }, 16);
                uiFontLanguage = Current;
                return uiFont;
            }
        }

        // Fusion Pixel is drawn on a 12 px grid: snap sizes so the glyphs stay crisp (12, 24, 36...).
        public static int PixelSize(int size) => Mathf.Max(12, Mathf.RoundToInt(size / 12f) * 12);

        public static Font NumberFont
        {
            get
            {
                if (numberFont == null) numberFont = Resources.Load<Font>("Fonts/DSEG/DSEG7Classic-Bold");
                return numberFont;
            }
        }
    }
}
