using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ToonBossRush.Boss;
using ToonBossRush.Combat;
using ToonBossRush.Player;

namespace ToonBossRush.Core
{
    /// <summary>
    /// 게임 전체 진행 단계. 기획서 4장 핵심 루프(시작 → 아레나 진입 → 보스 A → 보스 B → 클리어 화면)를
    /// 코드로 옮긴 상태값 — 콤보/회피/보스 패턴 같은 "전투 중" 세부 로직은 건드리지 않고,
    /// "지금 누구 차례인지" "다음으로 넘어갈지 게임오버로 갈지"만 관리한다.
    /// </summary>
    public enum GameFlowState
    {
        Intro,       // 런 시작 직후 짧은 대기 — 추후 "시작" UI/연출을 붙일 자리
        BossFight,   // 현재 인카운터(보스) 전투 중
        Transition,  // 보스 격파 → 다음 보스 활성화 사이의 짧은 대기 — 카메라 전환 등 연출 자리
        Clear,       // 모든 인카운터 격파
        GameOver     // 플레이어 사망
    }

    /// <summary>클리어/게임오버 확정 시 HUD·클리어 화면에 전달할 결과 데이터.</summary>
    [Serializable]
    public struct RunResult
    {
        public bool isVictory;
        public float clearTimeSeconds;
        public int hitCount;
        public int matchingCounterSuccessCount;
    }

    /// <summary>
    /// 순차 진행할 보스 1체 = 1 인카운터. rootToActivate는 보통 boss와 같은 오브젝트지만,
    /// 보스 모델+이펙트+스폰 위치를 한 프리팹으로 묶었다면 그 루트를 따로 지정해도 된다.
    /// (기획서 12장 리스크: "보스 2체" 대신 "보스 1체 3페이즈"로 스코프를 줄이게 되면,
    /// 이 리스트는 그냥 길이 1로 두고 페이즈 전환은 BossStateMachine 내부에서 처리 —
    /// GameFlowManager는 "최종 Health가 죽었는지"만 보므로 이 구조 변경 없이 그대로 대응됨)
    /// </summary>
    [Serializable]
    public class BossEncounter
    {
        [Tooltip("HUD에 표시할 보스 이름(기획서 7장 UI 항목) — HUD 연동 전이므로 지금은 기록만")]
        public string displayName = "Boss";
        public BossStateMachine boss;
        [Tooltip("비워두면 boss.gameObject를 그대로 활성/비활성 대상으로 사용")]
        public GameObject rootToActivate;

        public GameObject Root => rootToActivate != null ? rootToActivate : boss?.gameObject;
    }

    /// <summary>
    /// 게임 전체 흐름을 관리하는 최상위 로직. 씬에 빈 GameObject 하나("GameFlow")에 붙여서 사용.
    ///
    /// 전투(PlayerController/PlayerCombat)와 보스 AI(BossStateMachine)는 이미 각자 독립적으로
    /// 완결된 컴포넌트로 동작 중 — 이 매니저는 그 위에 얹혀서 "런(run) 단위" 진행만 담당한다.
    /// 접점은 딱 세 개뿐:
    ///   1) 활성 보스의 Health.OnDied → 다음 인카운터로 진행(또는 전부 끝났으면 Clear)
    ///   2) 플레이어 Health.OnDied → GameOver
    ///   3) 플레이어 Health.OnDamaged → 피격 횟수 집계(클리어 화면 통계용)
    /// 텔레그래프 매칭 카운터(기획서 5장, 아직 미구현)가 나중에 들어오면
    /// ReportMatchingCounterSuccess()만 호출하면 되도록 훅을 미리 열어둠.
    /// </summary>
    public class GameFlowManager : MonoBehaviour
    {
        public static GameFlowManager Instance { get; private set; }

        [Header("플레이어")]
        [SerializeField] private Health playerHealth;
        [SerializeField] private PlayerController playerController;
        [SerializeField] private Transform playerSpawnPoint;

        [Header("보스 인카운터 (진행 순서대로)")]
        [SerializeField] private List<BossEncounter> encounters = new List<BossEncounter>();

        [Header("타이밍")]
        [Tooltip("런 시작 후 첫 보스 전투 시작까지 대기(초) — '시작' UI/연출 붙일 자리")]
        [SerializeField] private float introDelaySeconds = 1.5f;
        [Tooltip("보스 격파 후 다음 보스 활성화까지 대기(초) — 카메라 전환 등 연출 자리")]
        [SerializeField] private float transitionDelaySeconds = 1.5f;

        public GameFlowState CurrentState { get; private set; } = GameFlowState.Intro;
        public int CurrentEncounterIndex { get; private set; } = -1;

        /// <summary>상태가 바뀔 때마다(Intro/BossFight/Transition/Clear/GameOver) 호출 — HUD 화면 전환 트리거용.</summary>
        public event Action<GameFlowState> OnStateChanged;
        /// <summary>Clear 또는 GameOver가 확정된 순간 1회 호출 — 클리어 화면이 이 결과로 통계를 표시.</summary>
        public event Action<RunResult> OnRunComplete;

        private float _runStartTime;
        private int _hitCount;
        private int _matchingCounterSuccessCount;
        private BossEncounter _activeEncounter;

        private void Awake()
        {
            Instance = this;
        }

        private void OnEnable()
        {
            if (playerHealth != null)
            {
                playerHealth.OnDamaged += HandlePlayerDamaged;
                playerHealth.OnDied += HandlePlayerDied;
            }
        }

        private void OnDisable()
        {
            if (playerHealth != null)
            {
                playerHealth.OnDamaged -= HandlePlayerDamaged;
                playerHealth.OnDied -= HandlePlayerDied;
            }
            UnsubscribeActiveEncounter();
        }

        private void Start()
        {
            StartRun();
        }

        /// <summary>
        /// 런을 처음부터 다시 시작(최초 시작 + 재도전 공용). 모든 인카운터를 비활성/초기화하고
        /// Intro부터 다시 진행한다. 게임오버 화면의 "다시 하기" 버튼이 이 메서드를 호출하면 됨(HUD 연동 예정).
        /// </summary>
        public void StartRun()
        {
            StopAllCoroutines();
            UnsubscribeActiveEncounter();

            _hitCount = 0;
            _matchingCounterSuccessCount = 0;
            CurrentEncounterIndex = -1;

            if (playerHealth != null) playerHealth.ResetHealth();
            if (playerController != null && playerSpawnPoint != null)
                playerController.Teleport(playerSpawnPoint.position, playerSpawnPoint.rotation);

            foreach (var e in encounters)
            {
                e.boss?.ResetEncounter();
                if (e.Root != null) e.Root.SetActive(false);
            }

            SetState(GameFlowState.Intro);
            StartCoroutine(RunIntro());
        }

        private IEnumerator RunIntro()
        {
            if (introDelaySeconds > 0f)
                yield return new WaitForSeconds(introDelaySeconds);

            _runStartTime = Time.time;
            AdvanceToNextEncounter();
        }

        private void AdvanceToNextEncounter()
        {
            UnsubscribeActiveEncounter();
            CurrentEncounterIndex++;

            if (CurrentEncounterIndex >= encounters.Count)
            {
                CompleteRun(isVictory: true);
                return;
            }

            _activeEncounter = encounters[CurrentEncounterIndex];
            if (_activeEncounter.Root != null) _activeEncounter.Root.SetActive(true);

            if (_activeEncounter.boss != null)
            {
                _activeEncounter.boss.BeginEncounter();
                _activeEncounter.boss.Health.OnDied += HandleActiveBossDied;
            }
            else
            {
                Debug.LogWarning($"[GameFlow] 인카운터 {CurrentEncounterIndex}({_activeEncounter.displayName})에 boss가 비어 있음 — 이 인카운터는 자동으로 넘어가지 않습니다.");
            }

            SetState(GameFlowState.BossFight);
        }

        private void UnsubscribeActiveEncounter()
        {
            if (_activeEncounter?.boss != null)
                _activeEncounter.boss.Health.OnDied -= HandleActiveBossDied;
            _activeEncounter = null;
        }

        private void HandleActiveBossDied()
        {
            UnsubscribeActiveEncounter();

            if (CurrentEncounterIndex + 1 >= encounters.Count)
            {
                CompleteRun(isVictory: true);
            }
            else
            {
                SetState(GameFlowState.Transition);
                StartCoroutine(RunTransition());
            }
        }

        private IEnumerator RunTransition()
        {
            if (transitionDelaySeconds > 0f)
                yield return new WaitForSeconds(transitionDelaySeconds);

            AdvanceToNextEncounter();
        }

        private void HandlePlayerDamaged(float ratio, Vector3 hitPoint)
        {
            // 전투 중 피격만 "피격 횟수"로 집계 — Transition 중 우연히 남은 히트박스에 맞는
            // 경우 등을 통계에서 제외하기 위한 안전장치.
            if (CurrentState != GameFlowState.BossFight) return;
            _hitCount++;
        }

        private void HandlePlayerDied()
        {
            if (CurrentState == GameFlowState.Clear || CurrentState == GameFlowState.GameOver) return;
            CompleteRun(isVictory: false);
        }

        /// <summary>
        /// 텔레그래프 매칭 카운터 시스템(기획서 5장, 아직 미구현)이 성공 판정을 내렸을 때
        /// 호출할 훅. 지금은 구현이 없어 아무도 호출하지 않지만, 나중에 그 시스템을
        /// 붙일 때 이 메서드 하나만 호출하면 클리어 화면 통계에 바로 반영됨.
        /// </summary>
        public void ReportMatchingCounterSuccess()
        {
            _matchingCounterSuccessCount++;
        }

        private void CompleteRun(bool isVictory)
        {
            StopAllCoroutines();
            UnsubscribeActiveEncounter();

            var result = new RunResult
            {
                isVictory = isVictory,
                clearTimeSeconds = Time.time - _runStartTime,
                hitCount = _hitCount,
                matchingCounterSuccessCount = _matchingCounterSuccessCount,
            };

            SetState(isVictory ? GameFlowState.Clear : GameFlowState.GameOver);

            // 2026-09-20: 클리어 화면 UI가 아직 없어서, 지금은 통계가 실제로 잘 모이는지
            // 콘솔에서 눈으로 확인하는 용도로 로그를 남긴다. HUD 붙이면 이 Debug.Log는
            // 그대로 둬도 되고(디버그용) 지워도 됨 — OnRunComplete 이벤트가 진짜 연동 지점.
            Debug.Log($"[GameFlow] {(isVictory ? "CLEAR" : "GAME OVER")} - time={result.clearTimeSeconds:F1}s, hits={result.hitCount}, matchingCounters={result.matchingCounterSuccessCount}");

            OnRunComplete?.Invoke(result);
        }

        private void SetState(GameFlowState state)
        {
            CurrentState = state;
            OnStateChanged?.Invoke(state);
        }
    }
}
