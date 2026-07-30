using System.Collections.Generic;

namespace Ashburn.Cutscenes
{
    /// <summary>
    /// First-pass story data from docs/대사_연출안.md.
    ///
    /// Kept in code for the prototype so level triggers only need an event id. Once the final UI art
    /// and portraits exist, this can move to ScriptableObjects without changing the trigger shape.
    /// </summary>
    public static class DialogueCatalog
    {
        static readonly Dictionary<string, DialogueLine[]> Lines = new()
        {
            ["intro_tv_001"] = new[]
            {
                Say("센틸 안내방송", "애슈번 복구 사전 조사 임무에 배정된 것을 환영합니다."),
                Say("센틸 안내방송", "애슈번은 약 20년 전 발생한 화재 및 유독가스 사고 이후 폐쇄된 주거단지입니다."),
                Say("센틸 안내방송", "귀하는 철거 전 남아 있는 마을 데이터를 회수하고, 주요 시설 전력을 확인해야 합니다."),
                Say("센틸 안내방송", "현장 내부에는 잔류 저주파와 비정상 음향 반사가 존재할 수 있습니다. 지급된 헤드셋을 반드시 착용하십시오."),
                Say("센틸 안내방송", "헤드셋은 손전등, 음향 방향 표시, 근거리 노이즈 상쇄 기능을 제공합니다."),
                Say("센틸 안내방송", "동료 헤드셋에서 발생한 신호는 초록색으로 표시됩니다. 초록색 신호는 안전한 협업 신호입니다."),
                Say("센틸 안내방송", "불필요한 대화, 고성, 장비 타격음은 현장 안전을 위해 삼가십시오."),
                Say("센틸 안내방송", "더 나은 내일을 위한 더 나은 집. 센틸이 함께합니다."),
            },
            ["container_start_001"] = new[]
            {
                Say("Grant", "안내방송 치고는 묘하게 협박 같지 않았어?"),
                Say("Nathan", "하청 계약서에 친절함까지 포함되진 않았나 봐."),
                Say("Grant", "음향 데이터는 네 담당, 전력은 내 담당. 빨리 끝내고 나가자."),
                Say("Nathan", "좋아. 헤드셋 신호부터 확인하고 회사 로비로 가자."),
            },
            ["corp_lobby_enter_001"] = new[]
            {
                Say("Nathan", "로비 도착. 내부 전원은 살아 있는데 출입 권한은 막혀 있어."),
                Say("Grant", "2층도 지하도 잠겼네. 회사가 열쇠는 안 줬고, 책임은 우리한테 주고."),
                Say("Nathan", "우측 사무실부터 확인하자. 관리 단말이 남아 있을 수도 있어."),
            },
            ["corp_first_warden_001"] = new[]
            {
                Say("Grant", "...저게 뭐야? 마네킹이야?"),
                Say("Nathan", "아니. 내부에서 소리가 나. 모터음... 아니, 숨소리 같은데."),
                Say("Grant", "움직였어. 방금 분명히 움직였어."),
                Say("소음 관리자", "정숙 기준 초과. 원인 제거 절차를 시작합니다."),
                Say("Nathan", "그랜트, 말하지 마. 천천히 뒤로-"),
                Say("Grant", "뛰어! 지금 당장!"),
            },
            ["corp_escape_001"] = new[]
            {
                Say("Nathan", "뒤 돌아보지 마!"),
                Say("Grant", "보고 싶지도 않아!"),
                Say("소음 관리자", "복도 소음 증가. 추적 우선순위 상승."),
            },
            ["corp_after_escape_001"] = new[]
            {
                Say("Grant", "회사는 저런 게 남아 있다고 말한 적 없어."),
                Say("Nathan", "화재 사고 기록부터 다시 봐야겠어. 저건 단순 보안 장비가 아니야."),
                Say("Grant", "병원에 기록이 남아 있을까? 사망자나 부상자 명단 같은 거."),
                Say("Nathan", "병원부터 가자. 공식 설명이 맞는지 확인해야 해."),
            },
            ["hospital_enter_001"] = new[]
            {
                Say("Nathan", "접수 시스템은 죽었는데, 내부 기록 장치는 아직 전원이 남아 있어."),
                Say("Grant", "병원 냄새가 안 나. 먼지 냄새도 아니고... 그냥 비어 있는 냄새야."),
                Say("Nathan", "응급실 쪽을 보자. 화재 사고면 그쪽에 흔적이 남았을 거야."),
            },
            ["hospital_record_001"] = new[]
            {
                Say("Nathan", "이상해. 화상 환자 기록보다 청각 손상 기록이 더 많아."),
                Say("Grant", "화재 사고라며. 왜 다들 귀에서 피가 났다는 기록이 있어?"),
                Say("Nathan", "신고 기록이 필요해. 병원은 결과만 남겼고, 시작점은 다른 데 있어."),
                Say("Grant", "경찰서. 신고 접수 기록을 보면 어디서 시작됐는지 알 수 있겠네."),
            },
            ["police_enter_001"] = new[]
            {
                Say("Grant", "여긴 병원보다 더 멀쩡해 보여서 더 싫은데."),
                Say("Nathan", "신고 접수실, 증거보관실. 둘 다 확인하자. 회사 이름이 나오는지 봐야 해."),
            },
            ["police_sentil_files_001"] = new[]
            {
                Say("Nathan", "센틸 관련 민원, 사고, 실종 신고... 전부 같은 변호사 이름으로 끝났어."),
                Say("Grant", "이 정도면 덮은 게 아니라 묻어버린 건데."),
                Say("Nathan", "층간소음, 가정폭력, 구조 요청. 전부 '음향 설비 오작동'으로 처리됐어."),
                Say("Grant", "애슈번은 사고 현장이 아니라 실험장이었네."),
            },
            ["police_fire_calls_001"] = new[]
            {
                Say("신고 기록", "21:14, 주유소 방향 폭발음 신고."),
                Say("신고 기록", "21:16, 다수 주민 비명 감지. 음성 신호 상쇄됨."),
                Say("신고 기록", "21:18, 구조 요청 분류 실패. 정숙 모드 전환."),
                Say("Grant", "주유소가 시작점이야. 신고가 거기서 제일 많이 들어왔어."),
                Say("Nathan", "그리고 그 뒤의 목소리는 사라졌어. 누가 끊은 거야."),
            },
            ["police_exit_001"] = new[]
            {
                Say("Grant", "다음은 주유소지?"),
                Say("Nathan", "응. 화재가 아니라면, 그 밤에 무엇이 처음 깨어났는지 봐야 해."),
            },
            ["gas_first_monster_001"] = new[]
            {
                Say("Grant", "회사에서 봤던 그거야. 여기에도 있어."),
                Say("Nathan", "움직이지 마. 시야보다 소리에 반응하는 것 같아."),
                Say("Grant", "우리가 말하면 듣는다는 뜻이지?"),
                Say("Nathan", "...지금 그 질문에 대답하면 안 될 것 같아."),
            },
            ["gas_lure_plan_001"] = new[]
            {
                Say("Nathan", "저기 봐. 작업복 주머니에 뭔가 빛나."),
                Say("Grant", "저 괴물 바로 옆인데."),
                Say("Nathan", "키카드일 수도 있어. 회사 2층 문, 그걸로 열릴지 몰라."),
                Say("Grant", "내가 반대쪽에서 소리 낼게. 네이선, 넌 낮게 움직여서 확인해."),
                Say("Nathan", "너무 오래 끌지 마."),
                Say("Grant", "나도 오래 있고 싶진 않아."),
            },
            ["gas_keycard_001"] = new[]
            {
                Say("Nathan", "센틸 키카드야. 오래됐지만 칩은 살아 있어."),
                Say("Grant", "회사 2층 계단실. 거기 잠금이 이 규격이었어."),
                Say("Nathan", "돌아가자. 이제 지하로 내려갈 단서를 찾을 수 있을 거야."),
            },
            ["corp2_floor_plan_001"] = new[]
            {
                Say("Nathan", "로비 도면에는 없던 공간이 있어. 지하 음향 저장 시설."),
                Say("Grant", "로비에서 내려가는 길은 막혀 있었어. 계단도, 엘리베이터도."),
                Say("Nathan", "2층 설비실에서 제어할 수 있을지도 몰라. 내려갈 방법을 찾아보자."),
            },
            ["corp2_elevator_broken_001"] = new[]
            {
                Say("Grant", "엘리베이터 전원부가 죽었어. 기계실에서 우회하면 살릴 수 있어."),
                Say("Nathan", "그동안 난 문서 더 찾아볼게. 지하 시설이 뭔지 알아야 해."),
                Say("Grant", "무전 열어둘게. 너무 멀어지면 잡음 올라온다."),
            },
            ["corp2_need_document_001"] = new[]
            {
                Say("Grant", "수리는 끝났는데 내려가는 층이 잠겨 있어."),
                Say("Nathan", "잠깐만. 지하 시설 접근 권한이 문서에 묶여 있는 것 같아."),
                Say("Grant", "그럼 출발 전에 확실히 알아보고 가자. 아래에서 뭘 만날지 모르니까."),
            },
            ["corp2_noise_system_doc_001"] = new[]
            {
                Say("Nathan", "이건 방음 설비가 아니야. 소리를 분류하고, 위험하다고 판단하면 제거하는 시스템이야."),
                Say("Grant", "제거라니. 소리를 지운다는 뜻이야?"),
                Say("Nathan", "문서 표현은 그래. 그런데 회사에서 본 그 장비가 현장 대응 장치라면..."),
                Say("Grant", "소리가 아니라 소리를 낸 사람을 지웠겠지."),
                Say("Nathan", "엘리베이터 권한 풀렸어. 지하로 내려갈 수 있어."),
            },
            ["hangar_enter_001"] = new[]
            {
                Say("Grant", "...하나가 아니었어."),
                Say("Nathan", "생산 시설이야. 현장 대응 장치가 아니라 병력처럼 보관했어."),
                Say("Grant", "난 전력실 찾아볼게. 네이선, 사무실 쪽 기록 확인해."),
                Say("Nathan", "알겠어. 전원 넣기 전에 정지 조건부터 찾아야 해."),
            },
            ["hangar_button_001"] = new[]
            {
                Say("Nathan", "버튼 하나 찾았어. 용도 표기가 지워져 있어."),
                Say("Grant", "여기 문 하나 열렸어. 전력실로 이어지는 것 같아."),
                Say("Nathan", "조심해. 아무 버튼이나 누르는 게 좋은 장소는 아니야."),
                Say("Grant", "이미 눌렀잖아. 다음 걱정으로 넘어가자."),
            },
            ["hangar_warden_doc_001"] = new[]
            {
                Say("Nathan", "정식 명칭은 소음 관리자. 일정 데시벨 이상의 원인을 찾아 직접 제거하도록 설계됐어."),
                Say("Grant", "그래서 우리가 말할 때마다 반응한 거야."),
                Say("Nathan", "오류 대비 장치가 있어. 시청 3층, 중앙 제어 장치. 모든 관리자를 정지시킬 수 있대."),
                Say("Grant", "그럼 전력만 복구하면 바로 시청으로 가자."),
            },
            ["hangar_power_ready_001"] = new[]
            {
                Say("Grant", "패널은 정상인데 메인 라인이 안 넘어가."),
                Say("Nathan", "아직 확인할 게 남았어. 이 시설이 뭘 깨우는지 알아야 해."),
                Say("Grant", "빨리 찾아. 여기 서 있는 것들이 전부 나를 보고 있는 기분이야."),
            },
            ["hangar_awakening_001"] = new[]
            {
                Say("시스템", "격납고 전력 복구. 정숙 관리 절차 재개."),
                Say("Grant", "네이선. 방금 전부 켜졌어."),
                Say("Nathan", "문 밖으로 가! 시청까지 가야 해!"),
                Say("소음 관리자들", "정숙 기준 초과. 원인 제거. 원인 제거. 원인 제거."),
            },
            ["cityhall_1f_enter_001"] = new[]
            {
                Say("Nathan", "문서에 따르면 장치는 3층 중앙 제어실에 있어."),
                Say("Grant", "아래층은 비어 있어. 오히려 그래서 싫다."),
                Say("Nathan", "바로 올라가자. 격납고에서 깨어난 것들이 오래 기다려주진 않을 거야."),
            },
            ["cityhall_2f_crouch_001"] = new[]
            {
                Say("Grant", "너무 많아. 뛰면 바로 끝이야."),
                Say("Nathan", "웅크려서 가자. 발소리를 최대한 죽여야 해."),
                Say("Grant", "말도 줄이고. 방금 말하면서 걔 하나가 고개 돌렸어."),
            },
            ["cityhall_3f_device_001"] = new[]
            {
                Say("남겨진 센틸 직원", "이 기록을 보는 사람이 있다면, 애슈번은 사고로 조용해진 게 아닙니다."),
                Say("남겨진 센틸 직원", "주유소 폭발 이후 소음 관리자들이 주민의 비명과 구조 요청을 위협 신호로 분류했습니다."),
                Say("남겨진 센틸 직원", "본사는 대피 방송 대신 최고 단계 정숙 모드를 지시했습니다."),
                Say("남겨진 센틸 직원", "경보는 상쇄됐고, 신고는 저장됐고, 사람들은 아무에게도 들리지 않았습니다."),
                Say("남겨진 센틸 직원", "시청 3층 장치는 모든 관리자의 정지 권한을 갖고 있습니다. 이걸 숨긴 건 저희입니다."),
                Say("남겨진 센틸 직원", "죄송합니다. 너무 늦었습니다. 하지만 아직 누군가는 이 소리를 들을 수 있습니다."),
            },
            ["ending_shutdown_001"] = new[]
            {
                Say("Grant", "끝난 거야?"),
                Say("Nathan", "적어도 저 장비들은 멈췄어."),
                Say("Grant", "회사에 보고하면 기록을 또 묻겠지."),
                Say("Nathan", "그럼 회사 말고, 들을 수 있는 곳에 보내자."),
                Say("시스템", "외부 송신 대기 중."),
                Say("Grant", "네가 음향 담당이잖아."),
                Say("Nathan", "그리고 넌 전력 담당이고. 아직 우리 일이 안 끝났어."),
            },
        };

        public static bool TryGet(string id, out DialogueLine[] lines) =>
            Lines.TryGetValue(id, out lines);

        static DialogueLine Say(string speaker, string text) => new(speaker, text);
    }
}
