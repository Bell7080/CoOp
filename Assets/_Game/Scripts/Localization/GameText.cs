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
            ["hud.powerLabel"] = "PW",
            ["hud.controls"] = "WASD 이동 · Shift 달리기 · Ctrl 웅크리기(숨기) · Space 점프(스킬체크) · 좌클릭 상호작용(꾹: 염력) · 우클릭 꾹: 돌리기 · Q 놓기 · 1 인벤토리 · E 코드 뽑기/넣기 · V 무전 · F 손전등 · F1 개발 도구",
            ["hud.discharged"] = "방전되었습니다 — 유령이 되어 동료를 관전합니다",
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
            ["proto.objective"] = "충전한 가전의 플러그를 송신구에 꽂으세요  {0} / {1} · 예상 정산 {2}",
            ["proto.settling"] = "정산 중…",
            ["proto.settlingBanner"] = "송신구가 가전제품을 빨아들이고 있다! 날아오는 물건 조심",
            ["proto.blackout"] = "암전! 우주선으로 돌아가세요",
            ["proto.objectiveReturn"] = "어둠 속에서 괴물을 피해 우주선으로 돌아가세요",
            ["proto.objectiveLaunch"] = "출발 버튼을 누르세요",
            ["proto.launching"] = "출발까지 {0}초 — 모두 우주선 안으로!",
            ["proto.escaped"] = "탈출 성공! 정산 {0}",
            ["proto.leftBehind"] = "우주선이 떠났습니다… 전류가 바닥나고 있다",
            ["proto.defeat"] = "전원 방전 — 패배",
            ["proto.reviveProgress"] = "재세동 {0}% — 동료가 시체를 때리거나 우주선으로 옮겨야 합니다",
            ["proto.spectating"] = "유령 관전 중: 플레이어 {0} · 좌/우클릭: 전환",
            ["proto.spectatingNobody"] = "유령 관전 중: 살아 있는 동료가 없습니다",
            ["proto.inventory"] = "인벤토리",
            ["proto.slotEmpty"] = "비어 있음",
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
            ["interact.generator"] = "[좌클릭 길게] 발전기 돌리기 · 스킬체크는 Space",
            ["interact.generatorStalled"] = "발전기가 터졌다! 잠시 후 다시 돌아갑니다",
            ["interact.generatorFull"] = "발전기 — 공용 전기가 가득 찼습니다",
            ["interact.charge"] = "[좌클릭 길게] {0} 충전하기 ({1}%)",
            ["interact.chargeStalled"] = "{0} 과부하! 잠시 멈춤 ({1}%)",
            ["interact.noPower"] = "{0} 충전 {1}% — 공용 전기가 없습니다. 발전기를 돌리세요",
            ["interact.handsFull"] = "이미 다른 물건을 들고 있습니다",
            ["interact.plug"] = "[좌클릭] {0} 플러그 꽂기",
            ["interact.transmitterStatus"] = "송신구 {0} / {1} · 예상 정산 {2} — 충전한 가전의 플러그를 들고 와서 꽂으세요",
            ["interact.transmitterCharge"] = "[좌클릭 길게] 송신구에 전력 넣기 ({0}%)",
            ["interact.transmitterNoPower"] = "송신구 전력 {0}% — 공용 전기가 없습니다",
            ["interact.transmitterStalled"] = "송신구 과부하! 잠시 멈춤 ({0}%)",
            ["interact.transmitterDone"] = "송신구 — 정산 완료",
            ["interact.carrying"] = "{0} 염력으로 드는 중 ({1}/{2}명) · 우클릭 꾹+마우스: 돌리기 · 좌클릭 떼면 놓기",
            ["interact.carryingLatched"] = "{0} 꺼내 드는 중 ({1}/{2}명) · 우클릭 꾹+마우스: 돌리기 · 좌클릭/Q: 놓기",
            ["interact.carryingPlug"] = "{0} 플러그 들고 있음 · 전선 {1} / {2}m · 송신구에 좌클릭 · Q: 내려놓기",
            ["interact.grab"] = "[좌클릭 꾹] {0} 염력으로 들기",
            ["interact.grabHeavy"] = "[좌클릭 꾹] {0} 같이 들기 ({1}/{2}명 — 모자라면 끌기만 됨)",
            ["interact.value"] = "예상 금액 {0} / {1}",
            ["interact.canStore"] = "숫자 키: 인벤토리에 보관",
            ["interact.plugTake"] = "[좌클릭] {0} 플러그 뽑아 들기 (전선 {1}m)",
            ["interact.plugHeldByOther"] = "{0} 플러그 — 다른 플레이어가 들고 있음",
            ["interact.plugSocketed"] = "{0} 플러그 — 송신구에 꽂혀 있음",
            ["interact.plugNotCharged"] = "{0}이(가) 충전되지 않았습니다 — 먼저 충전하세요",
            ["interact.doorCharge"] = "[좌클릭 길게] 문에 전력 넣기 ({0}%)",
            ["interact.doorNoPower"] = "닫힌 문 ({0}%) — 공용 전기가 없습니다. 발전기를 돌리세요",
            ["interact.doorOpen"] = "[좌클릭] 문 열기",
            ["interact.doorSwingOpen"] = "[좌클릭] 문 열기 (밀고 지나가도 열립니다)",
            ["interact.doorSwingClose"] = "[좌클릭] 문 닫기",
            ["cctv.pageMinimap"] = "미니맵 >",
            ["cctv.pageFeed"] = "< 카메라",
            ["cctv.minimapTitle"] = "미니맵 — 켜진 카메라가 있는 방만 보입니다 (초록: 아군, 빨강: 괴물)",
            ["interact.doorClose"] = "[좌클릭] 문 닫기",
            ["interact.doorBlocked"] = "문 사이에 누가 있어 닫을 수 없습니다",
            ["interact.doorDead"] = "전기가 없어 문이 움직이지 않습니다 — 발전기를 돌리세요",
            ["interact.launch"] = "[좌클릭] 출발 버튼 누르기",
            ["interact.launching"] = "출발까지 {0}초",
            ["interact.launchLocked"] = "출발 버튼 — 송신을 먼저 끝내야 합니다",
            ["supply.title"] = "지원 물품 주문",
            ["supply.medkit"] = "구급키트",
            ["supply.adrenaline"] = "아드레날린",
            ["supply.sending"] = "지원스테이션으로 전송 중",
            ["supply.waiting"] = "스테이션 도착 · 수령 대기",
            ["hover.supplyIdle"] = "지원스테이션 — 우주선 단말기에서 주문한 물품이 여기 도착",
            ["hover.medkitEffect"] = "체력 +40 회복",
            ["hover.adrenalineEffect"] = "20초 동안 달려도 지치지 않음",
            ["hover.appliance"] = "{0} · 가치 {1}",
            ["hover.applianceDamaged"] = "{0} · 가치 {1} (원래 {2})",
            ["hover.charged"] = "충전됨",
            ["hover.needsCharge"] = "충전 필요",
            ["hover.heavy"] = "{0}명이 들어야 함",
            ["hover.tiny"] = "가방에 넣을 수 있음",
            ["shop.title"] = "지원 물품 상점",
            ["shop.credits"] = "보유 ${0}",
            ["shop.confirm"] = "구매하시겠습니까?",
            ["shop.ok"] = "확인",
            ["shop.cancel"] = "취소",
            ["shop.ordering"] = "주문 중...",
            ["shop.bought"] = "{0} 구매 완료\n지원스테이션으로 전송 중",
            ["shop.noMoney"] = "잔액이 부족합니다",
            ["shop.busy"] = "배송 중인 물품이 있습니다\n스테이션에서 먼저 받아 가세요",
            ["shop.tooFar"] = "단말기에서 너무 멉니다",
            ["shop.hint"] = "항목을 눌러 구매 · 휠로 목록 이동",
            ["settle.target"] = "정산 목표  ${0}",
            ["settle.plugged"] = "연결됨  ${0}",
            ["settle.sockets"] = "콘센트 {0} / {1}",
            ["settle.needPower"] = "전력 공급 필요  {0}%",
            ["settle.rising"] = "리프트 상승 중...",
            ["settle.ready"] = "가전 코드를 연결하세요",
            ["settle.settling"] = "정산 중...",
            ["settle.done"] = "정산 완료",
            ["appliance.multitap2"] = "2구 멀티탭",
            ["appliance.multitap3"] = "3구 멀티탭",
            ["appliance.multitap5"] = "5구 멀티탭",
            ["appliance.multitap8"] = "8구 멀티탭",
            ["hover.multitap"] = "{0} · 꽂힘 {1}/{2} · 합계 ${3}",
            ["hover.multitapLive"] = "송신기에 연결됨 — 꽂힌 가전이 함께 정산됩니다",
            ["hover.multitapHint"] = "가전 코드를 들고 E로 꽂기 · 멀티탭 코드는 송신기에",
            ["hover.multitapEffect"] = "가전 여러 개를 송신기 콘센트 하나로 정산 (충전 불필요)",
            ["shop.soldOut"] = "품절입니다",
            ["shop.tabMedical"] = "의료품",
            ["shop.tabPower"] = "멀티탭",
            ["settle.short"] = "목표 미달 · 더 연결하세요",
            ["hud.plugTake"] = "코드 뽑기",
            ["hud.plugSocket"] = "코드 꽂기",
            ["hud.plugReturn"] = "코드 넣기",
            ["interact.corpse"] = "[좌클릭 연타] 플레이어 {0} 재세동 ({1}%) · [좌클릭 꾹] 시체 옮기기",
            ["interact.reviveOnly"] = "[좌클릭 연타] 플레이어 {0} 재세동 ({1}%)",
            ["interact.reviveHold"] = "[E 길게] 체력 {1}을 넣어 플레이어 {0} 부활 ({2}%)",
            ["interact.reviveWeak"] = "체력이 {0}보다 많아야 부활시킬 수 있습니다",
            ["interact.corpseCarry"] = "[좌클릭 꾹] 시체 옮기기",
            ["interact.shipMic"] = "우주선 방송 마이크 — [V 꾹] 모든 플레이어에게 방송",
            ["interact.cctvPanel"] = "[좌클릭] CCTV 보기 (연결 {0} / {1})",
            ["interact.cctvOnline"] = "{0} — 우주선 CCTV 패널에 연결됨",

            // CCTV view
            ["cctv.controls"] = "화면 끌기: 카메라 돌리기 · 휠: 확대 · 지도 클릭/A·D/숫자: 카메라 전환 · Q·Tab·Esc·우클릭: 나가기",
            ["cctv.noSignal"] = "신호 없음 — 밖에서 이 카메라에 전력을 넣어야 합니다",
            ["cctv.rec"] = "● REC",
            ["cctv.zoom"] = "확대 ×{0}",
            ["cctv.mapTitle"] = "CCTV 지도",
            ["cctv.mapShip"] = "우주선 (나)",
            ["corpse.name"] = "플레이어 {0}의 시체",
            ["interact.deviceCharge"] = "[좌클릭 꾹] {0} 켜기 ({1}%)",
            ["interact.deviceNoPower"] = "{0} 전력 {1}% — 공용 전기가 없습니다",
            ["interact.deviceOnline"] = "{0} — 작동 중",
            ["interact.deviceDetach"] = "[좌클릭 꾹] {0} 뜯어내기 ({1}%) — 꺼지지만 팔 수 있다",
            ["interact.deviceDetached"] = "{0} — 뜯겨 나감",
            ["interact.lampOn"] = "{0} — 켜져 있음",
            ["lamp.ceiling"] = "천장 전등",
            ["salvage.cctv"] = "뜯어낸 CCTV 카메라",
            ["salvage.lamp"] = "뜯어낸 전등",
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
            ["dev.power"] = "공용 전기 가득",
            ["dev.charge"] = "가전·기기 모두 충전",
            ["dev.toTransmitter"] = "송신구로 이동",
            ["dev.toShip"] = "우주선으로 이동",
            ["dev.close"] = "닫기",

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
