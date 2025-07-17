using UnityEngine;
using UnityEngine.Rendering.Universal;

public class TorchController : MonoBehaviour
{
    public Animator animator;
    public bool isLit = false;
    public Light2D torchLight; // Drag your Light 2D component here in the Inspector

    void Start()
    {
        // If you forgot to drag, try auto-assign (for child objects)
        if (animator == null)
            animator = GetComponent<Animator>();
        if (torchLight == null)
            torchLight = GetComponentInChildren<Light2D>();

        SetLit(isLit);
    }

    public void SetLit(bool lit)
    {
        isLit = lit;
        animator.SetBool("IsLit", isLit);

        if (torchLight != null)
        {
            torchLight.enabled = isLit;
        }
        else
        {
            Debug.LogWarning("TorchController: No Light2D component found!", this);
        }
    }
}
