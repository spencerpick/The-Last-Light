using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    public float moveSpeed = 5f;

    private Rigidbody2D rb;
    private Animator animator;
    private Vector2 moveInput;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();
    }

    void Update()
    {
        // Get Input
        moveInput.x = Input.GetAxisRaw("Horizontal");
        moveInput.y = Input.GetAxisRaw("Vertical");

        // Prevent diagonal speed boost
        moveInput = moveInput.normalized;

        // Set AnimState for Animator
        int animState = 0; // Idle
        if (moveInput.sqrMagnitude > 0.01f)
        {
            if (Mathf.Abs(moveInput.x) > Mathf.Abs(moveInput.y))
                animState = (moveInput.x > 0) ? 4 : 3; // 4 = right, 3 = left
            else
                animState = (moveInput.y > 0) ? 2 : 1; // 2 = up, 1 = down
        }
        animator.SetInteger("AnimState", animState);
    }

    void FixedUpdate()
    {
        rb.MovePosition(rb.position + moveInput * moveSpeed * Time.fixedDeltaTime);
    }
}
