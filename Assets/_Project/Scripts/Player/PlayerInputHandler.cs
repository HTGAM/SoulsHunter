using UnityEngine;
using UnityEngine.InputSystem;

namespace SoulsHunter.Player
{
    /// <summary>
    /// 키보드/마우스와 게임패드 입력을 한 곳에서 읽어 다른 컴포넌트에 넘겨준다.
    /// 키 배치는 코드에 정의되어 있어 별도 에셋 없이 동작한다.
    ///
    /// 키보드/마우스: WASD 이동, 마우스 시점, Shift 달리기, Space 구르기, Q/휠클릭 락온,
    ///                좌클릭 약공격, 우클릭 강공격, R 회복약, E 상호작용, Tab/I 가방
    /// 게임패드:      왼스틱 이동, 오른스틱 시점, B(○) 짧게=구르기 / 길게=달리기, R3 락온,
    ///                R1 약공격, R2 강공격, X(□) 회복약, Y(△) 상호작용, Select 가방
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

        [Header("Gamepad Dodge / Sprint")]
        [Tooltip("이 시간보다 짧게 눌렀다 떼면 구르기, 더 오래 누르면 달리기")]
        [SerializeField] float padHoldThreshold = 0.25f;

        InputAction move, lookMouse, lookStick, sprintKey, dodgeKey, padDodgeSprint, lockOn, lightAttack, heavyAttack,
                    useItem, interact, inventory;
        float padPressTime;

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

            sprintKey = new InputAction("Sprint", InputActionType.Button, "<Keyboard>/leftShift");
            dodgeKey = new InputAction("Dodge", InputActionType.Button, "<Keyboard>/space");
            padDodgeSprint = new InputAction("PadDodgeSprint", InputActionType.Button, "<Gamepad>/buttonEast");

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
        }

        void OnEnable()
        {
            foreach (var a in All()) a.Enable();
        }

        void OnDisable()
        {
            foreach (var a in All()) a.Disable();
            Move = Vector2.zero;
            LookDelta = Vector2.zero;
            SprintHeld = DodgePressed = LockOnPressed = LightAttackPressed = HeavyAttackPressed = false;
            UseItemPressed = InteractPressed = InventoryPressed = false;
        }

        void OnDestroy()
        {
            foreach (var a in All()) a.Dispose();
        }

        InputAction[] All() => new[] { move, lookMouse, lookStick, sprintKey, dodgeKey, padDodgeSprint, lockOn, lightAttack, heavyAttack,
                                       useItem, interact, inventory };

        void Update()
        {
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
            if (padDodgeSprint.WasPressedThisFrame()) padPressTime = now;
            bool padSprint = padDodgeSprint.IsPressed() && now - padPressTime >= padHoldThreshold;
            bool padDodge = padDodgeSprint.WasReleasedThisFrame() && now - padPressTime < padHoldThreshold;

            SprintHeld = sprintKey.IsPressed() || padSprint;
            DodgePressed = dodgeKey.WasPressedThisFrame() || padDodge;
            LockOnPressed = lockOn.WasPressedThisFrame();

            // 커서가 풀린 상태의 클릭은 '커서 다시 잠그기'용이므로 공격으로 치지 않는다
            bool cursorLocked = Cursor.lockState == CursorLockMode.Locked;
            LightAttackPressed = lightAttack.WasPressedThisFrame() && (cursorLocked || Gamepad.current?.rightShoulder.wasPressedThisFrame == true);
            HeavyAttackPressed = heavyAttack.WasPressedThisFrame() && (cursorLocked || Gamepad.current?.rightTrigger.wasPressedThisFrame == true);

            UseItemPressed = useItem.WasPressedThisFrame();
            InteractPressed = interact.WasPressedThisFrame();
            InventoryPressed = inventory.WasPressedThisFrame();
        }
    }
}
