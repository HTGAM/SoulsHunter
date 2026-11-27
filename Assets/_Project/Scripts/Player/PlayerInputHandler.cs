using UnityEngine;
using UnityEngine.InputSystem;

namespace SoulsHunter.Player
{
    /// <summary>
    /// 키보드/마우스와 게임패드 입력을 한 곳에서 읽어 다른 컴포넌트에 넘겨준다.
    /// 키 배치는 코드에 정의되어 있어 별도 에셋 없이 동작한다.
    ///
    /// 키보드/마우스: WASD 이동, 마우스 시점, Space 짧게=구르기 / 길게=달리기, Q/휠클릭 락온,
    ///                좌클릭 약공격, 우클릭 강공격(누르고 있으면 모으기), 마우스 옆 버튼 가드(누르는 순간 패링), F 무기 특수 행동,
    ///                R 회복약, E 상호작용,
    ///                Tab/I 가방, ESC 일시정지
    /// 게임패드:      왼스틱 이동, 오른스틱 시점, B(○) 짧게=구르기 / 길게=달리기, R3 락온,
    ///                R1 약공격, R2 강공격, L1 가드, L2 무기 특수 행동, X(□) 회복약, Y(△) 상호작용, Select 가방, Start 일시정지
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class PlayerInputHandler : MonoBehaviour
    {
        [Header("Look Sensitivity")]
        [Tooltip("마우스 1픽셀당 회전 각도(도)")]
        [SerializeField] float mouseSensitivity = 0.12f;
        [Tooltip("스틱을 끝까지 밀었을 때 초당 회전 각도(도)")]
        [SerializeField] float gamepadSensitivity = 200f;
        [SerializeField] bool invertY = false;

        [Header("Dodge / Sprint (Space, 게임패드 B)")]
        [Tooltip("이 시간보다 짧게 눌렀다 떼면 구르기, 더 오래 누르면 달리기")]
        [SerializeField] float padHoldThreshold = 0.25f;

        InputAction move, lookMouse, lookStick, dodgeSprint, lockOn, lightAttack, heavyAttack,
                    useItem, interact, inventory, guard, pause, special;
        float dodgeSprintPressTime;

        /// <summary>이동 입력 (길이 0~1)</summary>
        public Vector2 Move { get; private set; }
        /// <summary>이번 프레임 시점 회전량(도). x = 오른쪽, y = 위쪽</summary>
        public Vector2 LookDelta { get; private set; }
        public bool SprintHeld { get; private set; }
        public bool DodgePressed { get; private set; }
        public bool LockOnPressed { get; private set; }
        public bool LightAttackPressed { get; private set; }
        public bool HeavyAttackPressed { get; private set; }
        public bool UseItemPressed { get; private set; }
        public bool InteractPressed { get; private set; }
        public bool InventoryPressed { get; private set; }
        public bool LightAttackHeld { get; private set; }
        public bool HeavyAttackHeld { get; private set; }
        public bool SpecialPressed { get; private set; }
        public bool SpecialHeld { get; private set; }
        public bool GuardHeld { get; private set; }
        public bool GuardPressed { get; private set; }
        /// <summary>일시정지 버튼 (일시정지 중에도 읽힌다)</summary>
        public bool PausePressed { get; private set; }

        void Awake()
        {
            move = new InputAction("Move", InputActionType.Value, expectedControlType: "Vector2");
            move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");
            move.AddBinding("<Gamepad>/leftStick");

            lookMouse = new InputAction("LookMouse", InputActionType.Value, "<Mouse>/delta");
            lookStick = new InputAction("LookStick", InputActionType.Value, "<Gamepad>/rightStick");

            // 구르기/달리기: 한 버튼. 짧게 눌렀다 떼면 구르기, 꾹 누르고 있으면 달리기 (소울 시리즈 방식)
            dodgeSprint = new InputAction("DodgeSprint", InputActionType.Button, "<Keyboard>/space");
            dodgeSprint.AddBinding("<Gamepad>/buttonEast");

            lockOn = new InputAction("LockOn", InputActionType.Button, "<Keyboard>/q");
            lockOn.AddBinding("<Mouse>/middleButton");
            lockOn.AddBinding("<Gamepad>/rightStickPress");

            lightAttack = new InputAction("LightAttack", InputActionType.Button, "<Mouse>/leftButton");
            lightAttack.AddBinding("<Gamepad>/rightShoulder");
            heavyAttack = new InputAction("HeavyAttack", InputActionType.Button, "<Mouse>/rightButton");
            heavyAttack.AddBinding("<Gamepad>/rightTrigger");

            useItem = new InputAction("UseItem", InputActionType.Button, "<Keyboard>/r");
            useItem.AddBinding("<Gamepad>/buttonWest");
            interact = new InputAction("Interact", InputActionType.Button, "<Keyboard>/e");
            interact.AddBinding("<Gamepad>/buttonNorth");
            inventory = new InputAction("Inventory", InputActionType.Button, "<Keyboard>/tab");
            inventory.AddBinding("<Keyboard>/i");
            inventory.AddBinding("<Gamepad>/select");
            // 가드: 마우스 옆(엄지) 버튼 두 개 중 아무거나
            guard = new InputAction("Guard", InputActionType.Button, "<Mouse>/backButton");
            guard.AddBinding("<Mouse>/forwardButton");
            guard.AddBinding("<Gamepad>/leftShoulder");
            special = new InputAction("Special", InputActionType.Button, "<Keyboard>/f");
            special.AddBinding("<Gamepad>/leftTrigger");
            pause = new InputAction("Pause", InputActionType.Button, "<Keyboard>/escape");
            pause.AddBinding("<Gamepad>/start");
        }

        void OnEnable()
        {
            foreach (var a in All()) a.Enable();
        }

        void OnDisable()
        {
            foreach (var a in All()) a.Disable();
            ClearGameplayInput();
            PausePressed = false;
        }

        void ClearGameplayInput()
        {
            Move = Vector2.zero;
            LookDelta = Vector2.zero;
            SprintHeld = DodgePressed = LockOnPressed = LightAttackPressed = HeavyAttackPressed = false;
            UseItemPressed = InteractPressed = InventoryPressed = GuardHeld = GuardPressed = false;
            LightAttackHeld = HeavyAttackHeld = SpecialPressed = SpecialHeld = false;
        }

        void OnDestroy()
        {
            foreach (var a in All()) a.Dispose();
        }

        InputAction[] All() => new[] { move, lookMouse, lookStick, dodgeSprint, lockOn, lightAttack, heavyAttack,
                                       useItem, interact, inventory, guard, pause, special };

        void Update()
        {
            PausePressed = pause.WasPressedThisFrame();
            if (SoulsHunter.Core.GameState.IsPaused)
            {
                ClearGameplayInput();
                return;
            }

            Move = Vector2.ClampMagnitude(move.ReadValue<Vector2>(), 1f);

            // 커서가 풀려 있을 땐(ESC) 마우스로 시점이 돌지 않게 한다.
            Vector2 mouse = Cursor.lockState == CursorLockMode.Locked
                ? lookMouse.ReadValue<Vector2>() * mouseSensitivity
                : Vector2.zero;
            Vector2 stick = lookStick.ReadValue<Vector2>() * (gamepadSensitivity * Time.deltaTime);
            Vector2 look = mouse + stick;
            if (invertY) look.y = -look.y;
            LookDelta = look;

            // 게임패드 B: 짧게 누르면 구르기, 길게 누르면 달리기 (소울 시리즈 방식)
            float now = Time.unscaledTime;
            if (dodgeSprint.WasPressedThisFrame()) dodgeSprintPressTime = now;
            float held = now - dodgeSprintPressTime;
            bool sprint = dodgeSprint.IsPressed() && held >= padHoldThreshold;
            bool dodge = dodgeSprint.WasReleasedThisFrame() && held < padHoldThreshold;

            SprintHeld = sprint;
            DodgePressed = dodge;
            LockOnPressed = lockOn.WasPressedThisFrame();

            // 커서가 풀린 상태의 클릭은 '커서 다시 잠그기'용이므로 공격으로 치지 않는다
            bool cursorLocked = Cursor.lockState == CursorLockMode.Locked;
            LightAttackPressed = lightAttack.WasPressedThisFrame() && (cursorLocked || Gamepad.current?.rightShoulder.wasPressedThisFrame == true);
            HeavyAttackPressed = heavyAttack.WasPressedThisFrame() && (cursorLocked || Gamepad.current?.rightTrigger.wasPressedThisFrame == true);

            UseItemPressed = useItem.WasPressedThisFrame();
            InteractPressed = interact.WasPressedThisFrame();
            InventoryPressed = inventory.WasPressedThisFrame();
            GuardHeld = guard.IsPressed();
            LightAttackHeld = lightAttack.IsPressed() && (cursorLocked || Gamepad.current != null);
            HeavyAttackHeld = heavyAttack.IsPressed() && (cursorLocked || Gamepad.current != null);
            SpecialPressed = special.WasPressedThisFrame();
            SpecialHeld = special.IsPressed();
            GuardPressed = guard.WasPressedThisFrame();
        }
    }
}
