using UnityEngine;

public class FreeCam : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 20f;
    [SerializeField] private float fastSpeed = 60f;
    [SerializeField] private float lookSpeed = 2f;

    private float _yaw = 0f;
    private float _pitch = 0f;

    void Start()
    {
        _yaw = transform.eulerAngles.y;
        _pitch = transform.eulerAngles.x;
    }

    void Update()
    {
        // Right click to look around
        if (Input.GetMouseButton(1))
        {
            _yaw += Input.GetAxis("Mouse X") * lookSpeed;
            _pitch -= Input.GetAxis("Mouse Y") * lookSpeed;
            _pitch = Mathf.Clamp(_pitch, -89f, 89f);
            transform.eulerAngles = new Vector3(_pitch, _yaw, 0f);
        }

        // WASD to move, hold shift for fast mode
        float speed = Input.GetKey(KeyCode.LeftShift) ? fastSpeed : moveSpeed;
        Vector3 move = Vector3.zero;

        if (Input.GetKey(KeyCode.W)) move += transform.forward;
        if (Input.GetKey(KeyCode.S)) move -= transform.forward;
        if (Input.GetKey(KeyCode.A)) move -= transform.right;
        if (Input.GetKey(KeyCode.D)) move += transform.right;
        if (Input.GetKey(KeyCode.E)) move += Vector3.up;
        if (Input.GetKey(KeyCode.Q)) move -= Vector3.up;

        transform.position += move * speed * Time.deltaTime;
    }
}
