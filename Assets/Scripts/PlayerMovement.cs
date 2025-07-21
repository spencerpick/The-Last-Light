using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    [Header("Movement Speeds")]
    public float walkSpeed = 2f;
    public float quickSpeed = 3.5f;

    private Rigidbody2D rb;
    private Animator animator;
    private Vector2 moveInput;
    private Vector2 lastMoveDirection;
    private float currentSpeed;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();
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

        // Toggle between speeds
        currentSpeed = Input.GetKey(KeyCode.LeftShift) ? quickSpeed : walkSpeed;

        bool isMoving = moveInput.sqrMagnitude > 0.01f;
        animator.SetBool("IsMoving", isMoving);

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
