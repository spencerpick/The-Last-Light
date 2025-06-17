using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    public Transform target;        // Drag the Player here in the Inspector
    public float smoothSpeed = 5f;  // Adjust for how snappy you want the camera
    public Vector3 offset = new Vector3(0f, 0f, -10f); // Keep camera behind the scene

    void LateUpdate()
    {
        if (target != null)
        {
            Vector3 desiredPosition = target.position + offset;
            transform.position = Vector3.Lerp(transform.position, desiredPosition, smoothSpeed * Time.deltaTime);
        }
    }
}