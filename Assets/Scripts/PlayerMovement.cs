using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    [Header("Movement Speeds")]
    public float walkSpeed = 2f;
    public float quickSpeed = 3.5f;

    [Header("Sprint / Stamina")]
    public bool useStaminaForSprint = true;
    public KeyCode sprintKey = KeyCode.LeftShift;
    public float sprintStaminaPerSecond = 20f;     // drain while sprinting
    public float minStaminaToStartSprint = 5f;     // need at least this to begin sprinting

    private Rigidbody2D rb;
    private Animator animator;
    private Stamina stamina;
    private Vector2 moveInput;
    private Vector2 lastMoveDirection;
    private float currentSpeed;
    private bool isSprinting;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();
        stamina = GetComponent<Stamina>();
        lastMoveDirection = new Vector2(0, -1); // Default facing down
    }

    void Update()
    {
        // Input
        float inputX = Input.GetAxisRaw("Horizontal");
        float inputY = Input.GetAxisRaw("Vertical");

        // Prioritise axis to prevent diagonals sticking
        if (Mathf.Abs(inputX) > Mathf.Abs(inputY)) inputY = 0;
        else if (Mathf.Abs(inputY) > Mathf.Abs(inputX)) inputX = 0;

        moveInput = new Vector2(inputX, inputY).normalized;

        bool wantsSprint = Input.GetKey(sprintKey);
        bool isMoving = moveInput.sqrMagnitude > 0.01f;

        // Sprint logic (drain stamina only while moving)
        isSprinting = false;
        if (isMoving && wantsSprint)
        {
            if (!useStaminaForSprint)
            {
                isSprinting = true;
            }
            else if (stamina != null)
            {
                // must have enough stamina to start/continue sprinting
                if (stamina.currentStamina >= (isSprinting ? 0.5f : minStaminaToStartSprint))
                {
                    // try to drain (returns false if not enough this frame)
                    if (stamina.ConsumePerSecond(sprintStaminaPerSecond))
                    {
                        isSprinting = true;
                    }
                }
            }
        }

        // Toggle between speeds
        currentSpeed = isSprinting ? quickSpeed : walkSpeed;

        animator.SetBool("IsMoving", isMoving);
        animator.SetBool("IsSprinting", isSprinting);

        if (isMoving)
        {
            lastMoveDirection = moveInput;
        }

        animator.SetFloat("MoveX", lastMoveDirection.x);
        animator.SetFloat("MoveY", lastMoveDirection.y);
    }

    void FixedUpdate()
    {
        rb.MovePosition(rb.position + moveInput * currentSpeed * Time.fixedDeltaTime);
    }
}
