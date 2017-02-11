using System.Collections;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR

	using UnityEditor;
	[ExecuteInEditMode]
	[InitializeOnLoad]

#endif



public class spSync : MonoBehaviour {

	public enum HttpMethod {
		Automatic,
		Get,
		Post
	};

	public bool runInEditor = false;
	public bool logEvents = false;
	public bool useLocalUrl = true;
	public string localUrl = "http://localhost/spSync";
	public string serverUrl = "http://localhost/spSync";
	public string serverContextName = "apptest";
	public HttpMethod httpMethod = HttpMethod.Automatic;
	public string serverSecret = "ReplaceWithYourSecret";
	public bool preferServerDataOverLocal = true;
	public string syncTagName = "spSynced";

	public bool _toggle_nothing = false;


	// I suggest you DO NOT change the security setting, 
	// Hashing does not seem to be much of a bottleneck anyway, 
	// and giving away a backdoor to your machine is never great...
	SecurityOption security = SecurityOption.hashed; 
	public enum SecurityOption { unsecureLocalHashedServer, hashed, unsecure };



	// Change serializeData / unserializeAndMerge if you want to add data.

	// Todo: currently there is no "merge" (fine grained conflict handling)
	//       serializeData would need to keep track of the changes-per-object
	//       since last sync to server, and then in the case of conflict, 
	//       unserializeAndMerge should skip objects that has not been
	//       synced to server, and then the update of lastSyncedJsonGuess
	//       and the call to serializeData(true) should be skipped.

	string serializeData(bool justMerged = false) {
		GameObject[] objs = GameObject.FindGameObjectsWithTag(syncTagName);
		string fullJsonText = "{";
		foreach (GameObject obj in objs) {
			if(fullJsonText != "{") {
				fullJsonText += ",";
			}
			string jsonItemText = "{";
			var pos = obj.transform.localPosition;
			var rot = obj.transform.localRotation.eulerAngles;
			var scale = obj.transform.localScale;
			jsonItemText +=	  "\"pos\":[" + pos.x   + "," +  pos.y   + "," +  pos.z   + "],";
			jsonItemText +=   "\"rot\":[" + rot.x   + "," +  rot.y   + "," +  rot.z   + "],";
			jsonItemText += "\"scale\":[" + scale.x + "," +  scale.y + "," +  scale.z + "]";
			jsonItemText +="}";

			fullJsonText += "\""+obj.name+"\":"+jsonItemText;
		}
		fullJsonText += "}";		
		return fullJsonText;
	}

	void unserializeAndMerge(string jsonText) {
		GameObject[] objs = GameObject.FindGameObjectsWithTag(syncTagName);
		Dictionary<string,object> serverObjs = MiniJSON.Json.Deserialize(jsonText) as Dictionary<string,object>;
		foreach (GameObject obj in objs) {
			Dictionary<string,object> serverObj = serverObjs[obj.name] as Dictionary<string,object>;
			List<object> pos = (List<object>) serverObj["pos"];
			obj.transform.localPosition = new Vector3(
				System.Convert.ToSingle(pos[0]),
				System.Convert.ToSingle(pos[1]),
				System.Convert.ToSingle(pos[2]) ); 
			List<object> rot = (List<object>) serverObj["rot"];
			obj.transform.localRotation = Quaternion.Euler(
				System.Convert.ToSingle(rot[0]),
				System.Convert.ToSingle(rot[1]),
				System.Convert.ToSingle(rot[2]) ); 
			List<object> scale = (List<object>) serverObj["scale"];
			obj.transform.localScale = new Vector3(
				System.Convert.ToSingle(scale[0]),
				System.Convert.ToSingle(scale[1]),
				System.Convert.ToSingle(scale[2]) ); 
		}		
	}





	bool     initialServerCheckStarted = false;
	bool     initialServerCheckCompleted = false;

	int      lastVersion = 0;
	bool     lastVersionInvalid = false;
	string   lastServerJson = "";
	string   lastSyncedJsonGuess = "";
	string   lastJsonSentToServer = "";
	bool     serverStuffChanged = false;
	bool     listeningForUpdates = false;
	bool     waitingForWrite = false;

	void resetState() {
		initialServerCheckStarted = false;
		initialServerCheckCompleted = false;
		
		lastVersion = 0;
		lastVersionInvalid = false;
		lastServerJson = "";
		lastSyncedJsonGuess = "";
		lastJsonSentToServer = "";
		serverStuffChanged = false;
		listeningForUpdates = false;
		waitingForWrite = false;
	}


	static string sha256(string password)
	{
		System.Security.Cryptography.SHA256Managed crypt = new System.Security.Cryptography.SHA256Managed();
		System.Text.StringBuilder hash = new System.Text.StringBuilder();
		byte[] crypto = crypt.ComputeHash(System.Text.Encoding.UTF8.GetBytes(password), 0, System.Text.Encoding.UTF8.GetByteCount(password));
		foreach (byte theByte in crypto)
		{
			hash.Append(theByte.ToString("x2"));
		}
		return hash.ToString();
	}

	public string escape_uri(string str)
	{
		string str2 = "0123456789ABCDEF";
		int length = str.Length;
		System.Text.StringBuilder builder = new System.Text.StringBuilder(length * 2);
		int num3 = -1;
		while (++num3 < length)
		{
			char ch = str[num3];
			int num2 = ch;
			if ((((0x41 > num2) || (num2 > 90)) &&
				((0x61 > num2) || (num2 > 0x7a))) &&
				((0x30 > num2) || (num2 > 0x39)))
			{
				switch (ch)
				{
				case '@':
				case '*':
				case '_':
				case '+':
				case '-':
				case '.':
				case '/':
					goto Label_0125;
				}
				builder.Append('%');
				if (num2 < 0x100)
				{
					builder.Append(str2[num2 / 0x10]);
					ch = str2[num2 % 0x10];
				}
				else
				{
					builder.Append('u');
					builder.Append(str2[(num2 >> 12) % 0x10]);
					builder.Append(str2[(num2 >> 8) % 0x10]);
					builder.Append(str2[(num2 >> 4) % 0x10]);
					ch = str2[num2 % 0x10];
				}
			}
			Label_0125:
			builder.Append(ch);
		}
		return builder.ToString();
	}

	string substr(string s, int b, int e = -1) {
		int l=s.Length;
		b = b<l?b:l;
		return s.Substring(b, e == -1 ? l - b : (b+e<l?e:l-b) );
	}

	WWW createServerJob(string jobSpec) {
		bool useHash = security == SecurityOption.unsecureLocalHashedServer ? useLocalUrl == false : (security == SecurityOption.hashed ? true : false);
		bool usePost = httpMethod == HttpMethod.Automatic ? jobSpec.Length > 7000 : (httpMethod == HttpMethod.Post ? true : false);
		string jobname = useHash ? "hashJobs" : "plainJobs";
		WWW www = null;
		string hash = "";
		if(useHash) {
			hash = sha256(jobSpec + "HashingSalt_" + serverSecret);
		}
		if(usePost) {
			WWWForm wwwForm = new WWWForm();
			wwwForm.AddField(jobname, jobSpec);
			if(useHash) {
				wwwForm.AddField("hash",hash);
			}
			www = new WWW((useLocalUrl ? localUrl : serverUrl) + "/server_job.php", wwwForm);
		} else {
			www = new WWW((useLocalUrl ? localUrl : serverUrl) + "/server_job.php?" + jobname + "=" + escape_uri(jobSpec) + (useHash ? "&hash=" + hash : ""));
		}		
		return www;
	}

	void doLog(string msg) { Debug.Log(msg + "\n"); }
	string versionToString(int v) { return v.ToString().PadRight(11); }
//	int stringToVersion(string v) { return intParse(v); }
	int intParse(string i) {
		int outi;
		bool successfullyParsed = int.TryParse(i, out outi);
		if (!successfullyParsed){
			Debug.Log("intParse ERR "+i);
		}
		return outi;
	}

	IEnumerator spSync_saveToServer(string data, int version) {
		if(waitingForWrite == true) {
			if(logEvents) { doLog("DOUBLE WRITE!!!!!!!!!! "); }
		}
		waitingForWrite = true;
//		string job = "[{\"cmd\":\"set\",\"includeInfo\":1,\"relPath\":\"" + serverContextName + ".txt\"" +(version != 0 ? ", \"assertVersionId\":" + version : "")+ ", \"data\":\"" +  data.Replace("\"","\\\"") + "\"}]";
		string job = "[{\"cmd\":\"set\",\"relPath\":\"" + serverContextName + ".txt\"" +(version != 0 ? ", \"assertHeader\":" + versionToString(version) : "")+ ", \"data\":\"" + versionToString(version+1) +  data.Replace("\"","\\\"") + "\"}]";
		lastJsonSentToServer = data;
		WWW www = createServerJob(job);		
		yield return www;
		string text = www.text;

		//if(logEvents) { doLog("spSync_saveToServer return " + text); }
		if(substr(text,0,7) == "success") {
			var newVersion = version+1;
//			var newVersion = intParse(substr(text, 8, 11));

			// there can be several changes per second, use >= to be more robust
			//if(newVersion > lastVersion) {
			if(newVersion >= lastVersion) {
				if(logEvents) { doLog("successful save " + text); }
				lastVersion = newVersion;
				lastVersionInvalid = false;
				lastServerJson = lastJsonSentToServer;
				//lastLocalJson = lastJsonSentToServer;
				lastSyncedJsonGuess = lastJsonSentToServer;
			} else {
				if(logEvents) { doLog("successful save (outdated)" + text); }
			}
		} else if(substr(text,0,7) == "ver_err") {
//			var newVersion = intParse(substr(text, 8, 11));
//			if(newVersion > lastVersion) {
				lastVersionInvalid = true;
				if(logEvents) { doLog("unsuccessful save (flagged version invalid) " + text + "\n" + job); }
//			} else {
//				if(logEvents) { doLog("unsuccessful save (ignored) " + text + "\n" + job); }
//			}
		} else {
			if(logEvents) { doLog("SERVER ERROR! unsuccessful save " + text + "\n" + job); }
//			lastVersion = -1;
			lastVersionInvalid = true;
		}
		waitingForWrite = false;
	}

	IEnumerator spSync_GetDataFromURL(int version) {
		bool listening = version != 0;
//		string job = "[{\"cmd\":\"get\",\"includeInfo\":1,\"relPath\":\"" + serverContextName + ".txt\"" + (listening ? ", \"cometId\":" + version : "") + "}]";
		string job = "[{\"cmd\":\"get\",\"relPath\":\"" + serverContextName + ".txt\"" + (listening ? ", \"cometHeader\":" + versionToString(version) : "") + "}]";
		WWW www = createServerJob(job);

		yield return www;

		string text = www.text;
		//if(logEvents) { doLog("spSync_GetDataFromURL return " + text); }
		if(substr(text,0,8) == "success:") {
			var newVersion = intParse(substr(text, 8, 11));
			if(lastVersionInvalid || newVersion > lastVersion) {
				lastVersion = newVersion;
				lastVersionInvalid = false;
				var newServerJson = substr(text, 8+11);

				// when we write to the server, the current listening hook
				// might return the data we sent BEFORE we receive the result 
				// from spSync_saveToServer, so we need to make sure we don't 
				// flag our own writes as "serverStuffChanged"
				if(newServerJson != lastJsonSentToServer) {
					if(logEvents) { doLog("server change registered, new:"+newServerJson); }
					if(logEvents) { doLog("server change registered, old:"+lastJsonSentToServer); }

					lastServerJson = newServerJson;
					serverStuffChanged = true;
				} else {
					if(waitingForWrite) { 
						// this is actually a write success
						if(logEvents) { doLog("comet case write success"); }
						lastServerJson = lastJsonSentToServer;
						//lastLocalJson = lastJsonSentToServer;
						lastSyncedJsonGuess = lastJsonSentToServer; 
						waitingForWrite = false;
					}
				}
			} else {
				if(logEvents) { doLog("skipping (version same or older) " + newVersion + " " + lastVersion); }
			}
			if(logEvents) { doLog("successful " + (listening?"comet":"get") + job); }
		} else {
			if(logEvents) { doLog("unsuccessful " + (listening?"comet":"get") + www.text + job); }
		}
		if(listening) {
			// done listening
			listeningForUpdates = false;
		}
		initialServerCheckCompleted = true;
	}



	bool spSync_Sync() {
		bool sceneWasChanged = false;
	//	if(Time.frameCount%16 != 0) {
	//		return;
	//	}

		if(!initialServerCheckStarted) {
			
			initialServerCheckStarted = true;
			wrapStartCoroutine(spSync_GetDataFromURL(0));

		} else if(initialServerCheckCompleted) {
					
			bool syncToServer = false;
			bool syncFromServer = false;

			if(lastServerJson == "" && lastVersion == 0) {
				
				// file seem to be missing, sync up to create
				if(logEvents) { doLog("MISSING CONTEXT, database will created!"); }
				syncToServer = true;

			} else if(lastVersionInvalid) {

				// syncing to server failed, refresh
				if(logEvents) { doLog("(version invalid) doing version refresh"); }
				wrapStartCoroutine(spSync_GetDataFromURL(0));
			}

			if(!listeningForUpdates && !lastVersionInvalid && lastVersion != 0) {
				// listen to future updates
				if(logEvents) { doLog("start listen"); }
				listeningForUpdates = true;
				wrapStartCoroutine(spSync_GetDataFromURL(lastVersion));
			}

			if(!waitingForWrite) {

				string fullJsonText = serializeData();

				bool localStuffChanged = fullJsonText != lastSyncedJsonGuess;

				//bool localStuffChanged = false;
				//if(lastLocalJson != "" && fullJsonText != lastLocalJson) {
				//	lastLocalJson = fullJsonText;
				//	localStuffChanged = true;
				//}

				if(serverStuffChanged && localStuffChanged) {
					if(logEvents) { doLog("Conflict detected, resolve by " + (preferServerDataOverLocal?"server":"local")); }
					if(preferServerDataOverLocal) {
						syncFromServer = true;
					} else {
						syncToServer = true;
					}
				} else if(serverStuffChanged) {
					syncFromServer = true;
				} else if(localStuffChanged) {
					//if(logEvents) { doLog("localStuff " + fullJsonText); }
					//if(logEvents) { doLog("localStuff " + lastSyncedJsonGuess); }
					syncToServer = true;
				}


				if(syncFromServer) {
					if(lastServerJson == fullJsonText) {
						if(logEvents) { doLog("sync from server skip (already identical) " + fullJsonText); }
					} else {
						if(logEvents) { doLog("sync from server (unserializeAndMerge) " + lastServerJson); }
						unserializeAndMerge(lastServerJson);
						lastSyncedJsonGuess = serializeData(true);
						sceneWasChanged = true;
					}
				}

				if(syncToServer) {
					if(lastVersionInvalid) {
						if(logEvents) { doLog("sync to server skipped (version invalid, waiting for new reversion)"); }
	//				} else if(lastVersion == -2) {
	//					if(logEvents) { doLog("sync to server skipped (still waiting for return)"); }
					} else if(waitingForWrite) {
						if(logEvents) { doLog("sync to server skipped (waitingForWrite) " + lastVersion); }
					} else {
						if(logEvents) { doLog("sync to server begin " + lastVersion + " " + fullJsonText + " (" + lastSyncedJsonGuess + ")"); }
						wrapStartCoroutine(spSync_saveToServer(fullJsonText, lastVersion));

	//					lastVersion = -2;

						if(lastServerJson == "") {
							lastServerJson = fullJsonText;
						}
					}
				}

				serverStuffChanged = false;
			}
		}
		return sceneWasChanged;
	}



	void wrapStartCoroutine(IEnumerator cr) {
		StartCoroutine(cr);
/*		
#if UNITY_EDITOR		
		if(EditorApplication.isPlaying) {
			StartCoroutine(cr);
		} else {
			Swing.Editor.EditorCoroutine.start(cr);
		}
#else
		StartCoroutine(methodName, value);
#endif
*/
	}


#if UNITY_EDITOR

	// ---- EditorUpdate wrapper ---- 
	// Countinous callback in editor
	static spSync currentInstance = null;
	void OnEnable() {
		resetState();
		currentInstance = this;
	}
	void OnDisable() {
		currentInstance = null;
	}
	static void _EditorUpdate () {
		if(currentInstance) {
			currentInstance.EditorUpdate();
		}
	}
	static spSync()
	{
		EditorApplication.update += _EditorUpdate;
	}		
	// ------------------------------




	void EditorUpdate () {
		if(runInEditor && !EditorApplication.isPlaying) {
			bool sceneWasChanged = spSync_Sync();
//			if(sceneWasChanged) {
//				UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
				EditorUtility.SetDirty(this);
//			}
		}
	}

#endif


	void Start() {
		Application.runInBackground = true; // when testing using multiple unity windows, this is needed for them to update when not focused
	}



	void Update () {
#if UNITY_EDITOR		

		if(EditorApplication.isPlaying) {
			spSync_Sync();
		}

#else
		spSync_Sync();
#endif
	}

}
