using UnityEngine;

namespace Seb.Fluid.Demo
{
	public class OrbitCam : MonoBehaviour
	{
		public float moveSpeed = 3;
		public float rotationSpeed = 220;
		public float zoomSpeed = 0.1f;
		public Vector3 pivot;
		Vector3 mousePosOld;
		bool hasFocusOld;
		public float focusDst = 1f;
		Vector3 lastCtrlPivot;

		float lastLeftClickTime = float.MinValue;
		private Vector2 rightClickPos;

		private Vector3 startPos;
		Quaternion startRot;

		void Start()
		{
			startPos = transform.position;
			startRot = transform.rotation;
		}

		void Update()
		{
			if (Application.isFocused != hasFocusOld)
			{
				hasFocusOld = Application.isFocused;
				mousePosOld = Input.mousePosition;
			}

			// Reset view on double click
			if (Input.GetMouseButtonDown(0))
			{
				if (Time.time - lastLeftClickTime < 0.2f)
				{
					transform.position = startPos;
					transform.rotation = startRot;
				}

				lastLeftClickTime = Time.time;
			}

			float dstWeight = transform.position.magnitude;
			Vector2 mouseMove = Input.mousePosition - mousePosOld;
			mousePosOld = Input.mousePosition;
			float mouseMoveX = mouseMove.x / Screen.width;
			float mouseMoveY = mouseMove.y / Screen.width;
			Vector3 move = Vector3.zero;

			if (Input.GetMouseButton(2))
			{
				move += Vector3.up * mouseMoveY * -moveSpeed * dstWeight;
				move += Vector3.right * mouseMoveX * -moveSpeed * dstWeight;
			}

			if (Input.GetMouseButtonDown(0))
			{
				lastCtrlPivot = transform.position + transform.forward * focusDst;
			}

			if (Input.GetMouseButton(0))
			{
				Vector3 activePivot = Input.GetKey(KeyCode.LeftAlt) ? transform.position : pivot;
				if (Input.GetKey(KeyCode.LeftControl))
				{
					activePivot = lastCtrlPivot;
				}

				transform.RotateAround(activePivot, transform.right, mouseMoveY * -rotationSpeed);
				transform.RotateAround(activePivot, Vector3.up, mouseMoveX * rotationSpeed);
			}

			transform.Translate(move);

			//Scroll to zoom
			float mouseScroll = Input.mouseScrollDelta.y;
			if (Input.GetMouseButtonDown(1))
			{
				rightClickPos = Input.mousePosition;
			}

			if (Input.GetMouseButton(1))
			{
				Vector2 delta = (Vector2)Input.mousePosition - rightClickPos;
				rightClickPos = Input.mousePosition;
				mouseScroll = delta.magnitude * Mathf.Sign(Mathf.Abs(delta.x) > Mathf.Abs(delta.y) ? delta.x : -delta.y) / Screen.width * zoomSpeed * 100;
			}

			transform.Translate(Vector3.forward * mouseScroll * zoomSpeed * dstWeight);
		}

		void OnDrawGizmosSelected()
		{
			Gizmos.color = Color.red;
			// Gizmos.DrawWireSphere(pivot, 0.15f);
		}
	}
}