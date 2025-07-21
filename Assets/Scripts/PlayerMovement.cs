using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    public float moveSpeed = 5f;

    private Rigidbody2D rb;
    private Animator animator;
    private Vector2 moveInput;
    private Vector2 lastMoveDirection;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();
        lastMoveDirection = new Vector2(0, -1); // Default to facing down
    }

    void Update()
    {
        // Read input
        float inputX = Input.GetAxisRaw("Horizontal");
        float inputY = Input.GetAxisRaw("Vertical");

        // Prioritize vertical over horizontal if both pressed
        if (Mathf.Abs(inputX) > Mathf.Abs(inputY))
        {
            inputY = 0;
        }
        else if (Mathf.Abs(inputY) > Mathf.Abs(inputX))
        {
            inputX = 0;
        }

        moveInput = new Vector2(inputX, inputY).normalized;

        bool isMoving = moveInput.sqrMagnitude > 0.01f;
        animator.SetBool("IsMoving", isMoving);

        if (isMoving)
        {
            lastMoveDirection = moveInput;
        }

        // Set direction for both idle and walk
        animator.SetFloat("MoveX", lastMoveDirection.x);
        animator.SetFloat("MoveY", lastMoveDirection.y);
    }

    void FixedUpdate()
    {
        rb.MovePosition(rb.position + moveInput * moveSpeed * Time.fixedDeltaTime);
    }
}
