using UnityEngine;

public class TorchInteractor : MonoBehaviour
{
    public float interactRange = 1.5f;
    public LayerMask torchLayer;

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.E))
        {
            Debug.Log("E pressed, checking for torch nearby...");
            Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, interactRange, torchLayer);
            Debug.Log("Found " + hits.Length + " collider(s) in range.");

            foreach (Collider2D hit in hits)
            {
                TorchController torch = hit.GetComponent<TorchController>();
                if (torch != null && !torch.isLit)
                {
                    Debug.Log("Unlit torch found! Lighting now.");
                    torch.SetLit(true);
                    break;
                }
                else
                {
                    Debug.Log("Torch already lit or TorchController missing.");
                }
            }
        }
    }

    // Optional: Draw gizmo for interact range in the editor
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, interactRange);
    }
}
