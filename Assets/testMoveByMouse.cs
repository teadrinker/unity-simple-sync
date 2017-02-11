using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class testMoveByMouse : MonoBehaviour {

	// Use this for initialization
	void Start () {
		
	}

	Vector3 prevPos;
	// Update is called once per frame
	void Update () {
		Vector3 pos = Input.mousePosition;
		if(Input.GetMouseButton(0)) {
			Vector3 diff = pos - prevPos;
			gameObject.transform.localPosition = gameObject.transform.localPosition + diff*0.008f;
		} 			
		prevPos = pos;
	}
}
