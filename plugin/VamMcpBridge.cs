using System;
using UnityEngine;
using SimpleJSON;
using MVR.FileManagementSecure;

namespace MVRPlugin {
	// Session plugin: add under Session Plugins so it survives scene load.
	public class VamMcpBridge : MVRScript {

		protected string vamRoot;
		protected string bridgeDir;
		protected string commandPath;
		protected string resultPath;
		protected string statusPath;
		protected string previewPath;
		protected string lastCommandId = "";
		protected float nextPoll;
		protected float nextStatus;
		protected bool bridgeEnabled = true;
		protected JSONStorableBool enabledStore;
		protected JSONStorableString statusStore;

		public override void Init() {
			try {
				vamRoot = Application.dataPath + "/..";
				bridgeDir = "Saves/PluginData/vam-mcp";
				FileManagerSecure.CreateDirectory(bridgeDir);
				commandPath = bridgeDir + "/command.json";
				resultPath = bridgeDir + "/result.json";
				statusPath = bridgeDir + "/status.json";
				previewPath = bridgeDir + "/preview.png";

				enabledStore = new JSONStorableBool("enabled", true, OnEnabledChanged);
				RegisterBool(enabledStore);
				CreateToggle(enabledStore);

				statusStore = new JSONStorableString("status", "");
				CreateTextField(statusStore);

				SetStatus("ready  root=" + vamRoot);
				SuperController.LogMessage("VamMcpBridge ready. Bridge dir: " + bridgeDir);
				WriteStatusFile("idle", "");
			}
			catch (Exception e) {
				SuperController.LogError("VamMcpBridge.Init: " + e);
			}
		}

		protected void OnEnabledChanged(bool val) {
			bridgeEnabled = val;
			if (bridgeEnabled) {
				SetStatus("enabled");
			} else {
				SetStatus("paused");
			}
		}

		void Update() {
			try {
				if (!bridgeEnabled) {
					return;
				}
				if (Time.unscaledTime >= nextStatus) {
					nextStatus = Time.unscaledTime + 2f;
					WriteStatusFile("idle", "");
				}
				if (Time.unscaledTime < nextPoll) {
					return;
				}
				nextPoll = Time.unscaledTime + 0.2f;
				PollCommand();
			}
			catch (Exception e) {
				SuperController.LogError("VamMcpBridge.Update: " + e);
			}
		}

		protected void PollCommand() {
			if (!FileManagerSecure.FileExists(commandPath)) {
				return;
			}

			string text = "";
			try {
				text = SuperController.singleton.ReadFileIntoString(commandPath);
			}
			catch {
				return;
			}

			if (text == null || text == "") {
				return;
			}

			JSONNode parsed = JSON.Parse(text);
			if (parsed == null) {
				return;
			}
			JSONClass cmd = parsed.AsObject;
			if (cmd == null) {
				return;
			}

			string id = "";
			if (cmd["id"] != null) {
				id = cmd["id"].Value;
			}
			if (id == null || id == "" || id == lastCommandId) {
				return;
			}

			lastCommandId = id;
			string op = "";
			if (cmd["op"] != null) {
				op = cmd["op"].Value;
			}
			SetStatus("running " + op + " id=" + id);
			WriteStatusFile("running", op);

			try {
				JSONClass result = Dispatch(cmd);
				result["id"] = id;
				if (result["ok"] == null) {
					result["ok"] = "true";
				}
				SuperController.singleton.SaveJSON(result, resultPath);
				SetStatus("ok " + op + " id=" + id);
			}
			catch (Exception e) {
				JSONClass err = new JSONClass();
				err["id"] = id;
				err["ok"] = "false";
				err["op"] = op;
				err["error"] = e.Message;
				SuperController.singleton.SaveJSON(err, resultPath);
				SetStatus("error " + op + ": " + e.Message);
				SuperController.LogError("VamMcpBridge: " + e);
			}
		}

		protected JSONClass Dispatch(JSONClass cmd) {
			string op = "";
			if (cmd["op"] != null) {
				op = cmd["op"].Value;
			}
			JSONClass result = new JSONClass();
			result["op"] = op;
			result["ok"] = "true";

			if (op == "ping" || op == "status") {
				result["data"] = StatusPayload();
				return result;
			}

			if (op == "list_persons") {
				result["data"] = ListPersons();
				return result;
			}

			if (op == "scene_info") {
				result["data"] = SceneInfo();
				return result;
			}

			if (op == "capture_view") {
				string path = CapturePreview();
				JSONClass data = new JSONClass();
				data["path"] = path;
				result["data"] = data;
				return result;
			}

			if (op == "remove_person") {
				Atom person = RequiredPerson(cmd);
				string uid = person.uid;
				SuperController.singleton.RemoveAtom(person);
				JSONClass data = new JSONClass();
				data["removed"] = uid;
				result["data"] = data;
				return result;
			}

			if (op == "set_person_on") {
				Atom person = RequiredPerson(cmd);
				bool on = true;
				if (cmd["on"] != null) {
					on = cmd["on"].AsBool;
				}
				// Atom.on is read-only; ToggleOn() flips the live on/off state.
				if (person.on != on) {
					person.ToggleOn();
				}
				JSONClass data = new JSONClass();
				data["person"] = person.uid;
				if (on) {
					data["on"] = "true";
				} else {
					data["on"] = "false";
				}
				result["data"] = data;
				return result;
			}

			if (op == "add_person") {
				string uid = "MCPPerson";
				if (cmd["uid"] != null && cmd["uid"].Value != "") {
					uid = cmd["uid"].Value;
				}
				SuperController.singleton.StartCoroutine(SuperController.singleton.AddAtomByType("Person", uid, true));
				JSONClass data = new JSONClass();
				data["uid"] = uid;
				data["started"] = "true";
				result["data"] = data;
				return result;
			}

			if (op == "load_scene") {
				string path = RequiredPath(cmd);
				bool merge = false;
				if (cmd["merge"] != null) {
					merge = cmd["merge"].AsBool;
				}
				if (merge) {
					SuperController.singleton.LoadMerge(path);
				} else {
					SuperController.singleton.Load(path);
				}
				JSONClass data = new JSONClass();
				data["path"] = path;
				if (merge) {
					data["merge"] = "true";
				} else {
					data["merge"] = "false";
				}
				result["data"] = data;
				return result;
			}

			if (op == "load_look") {
				Atom person = RequiredPerson(cmd);
				string path = RequiredPath(cmd);
				RestorePreset(person, path);
				JSONClass data = new JSONClass();
				data["person"] = person.uid;
				data["path"] = path;
				data["kind"] = "look";
				result["data"] = data;
				return result;
			}

			if (op == "load_clothing") {
				Atom person = RequiredPerson(cmd);
				string path = RequiredPath(cmd);
				RestorePreset(person, path, false);
				JSONClass data = new JSONClass();
				data["person"] = person.uid;
				data["path"] = path;
				data["kind"] = "clothing";
				result["data"] = data;
				return result;
			}

			if (op == "load_pose") {
				string path = RequiredPath(cmd);
				string personArg = "";
				if (cmd["person"] != null) {
					personArg = cmd["person"].Value;
				}
				JSONArray applied = new JSONArray();
				if (personArg == "all") {
					foreach (Atom atom in SuperController.singleton.GetAtoms()) {
						if (atom == null || atom.type != "Person") {
							continue;
						}
						RestorePreset(atom, path);
						applied.Add(atom.uid);
					}
					if (applied.Count == 0) {
						throw new Exception("no Person atom in the current scene");
					}
				} else {
					Atom person = RequiredPerson(cmd);
					RestorePreset(person, path);
					applied.Add(person.uid);
				}
				JSONClass data = new JSONClass();
				data["path"] = path;
				data["kind"] = "pose";
				data["persons"] = applied;
				result["data"] = data;
				return result;
			}

			if (op == "list_expressions") {
				Atom person = RequiredPerson(cmd);
				result["data"] = ListExpressions(person);
				return result;
			}

			if (op == "set_expression") {
				Atom person = RequiredPerson(cmd);
				result["data"] = SetExpression(person, cmd);
				return result;
			}

			if (op == "lock_head") {
				Atom person = RequiredPerson(cmd);
				bool locked = true;
				if (cmd["locked"] != null) {
					locked = cmd["locked"].AsBool;
				}
				result["data"] = LockHead(person, locked);
				return result;
			}

			if (op == "move_person") {
				Atom person = RequiredPerson(cmd);
				result["data"] = MovePerson(person, cmd);
				return result;
			}

			if (op == "get_position") {
				Atom person = RequiredPerson(cmd);
				result["data"] = PersonPosition(person);
				return result;
			}

			throw new Exception("unknown op: " + op);
		}

		protected string RequiredPath(JSONClass cmd) {
			string path = "";
			if (cmd["path"] != null) {
				path = cmd["path"].Value;
			}
			if (path == null || path == "") {
				throw new Exception("missing path");
			}
			return path;
		}

		protected Atom RequiredPerson(JSONClass cmd) {
			string uid = "";
			if (cmd["person"] != null) {
				uid = cmd["person"].Value;
			}
			if (uid == null || uid == "") {
				uid = FirstPersonUid();
			}
			if (uid == null || uid == "") {
				throw new Exception("no Person atom in the current scene");
			}

			Atom atom = SuperController.singleton.GetAtomByUid(uid);
			if (atom == null) {
				throw new Exception("person not found: " + uid);
			}
			if (atom.type != "Person") {
				throw new Exception("atom is not a Person: " + uid);
			}
			return atom;
		}

		protected string FirstPersonUid() {
			foreach (Atom atom in SuperController.singleton.GetAtoms()) {
				if (atom != null && atom.type == "Person") {
					return atom.uid;
				}
			}
			return "";
		}

		protected string CapturePreview() {
			Camera cam = null;
			try {
				cam = SuperController.singleton.MonitorCenterCamera;
			}
			catch {
			}
			if (cam == null) {
				Camera[] cams = Camera.allCameras;
				if (cams != null && cams.Length > 0) {
					cam = cams[0];
				}
			}
			if (cam == null) {
				throw new Exception("no camera to capture");
			}

			int w = 1280;
			int h = 720;
			RenderTexture rt = new RenderTexture(w, h, 24);
			RenderTexture oldTarget = cam.targetTexture;
			RenderTexture oldActive = RenderTexture.active;
			Texture2D tex = null;
			try {
				cam.targetTexture = rt;
				cam.Render();
				RenderTexture.active = rt;
				tex = new Texture2D(w, h, TextureFormat.RGB24, false);
				tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
				tex.Apply();
				byte[] bytes = tex.EncodeToPNG();
				FileManagerSecure.WriteAllBytes(previewPath, bytes);
			}
			finally {
				cam.targetTexture = oldTarget;
				RenderTexture.active = oldActive;
				if (tex != null) {
					Destroy(tex);
				}
				rt.Release();
			}
			return previewPath;
		}

		protected string PersonCharacter(Atom atom) {
			try {
				JSONStorable geo = atom.GetStorableByID("geometry");
				if (geo == null) {
					return "";
				}
				return geo.GetStringParamValue("character");
			}
			catch {
				return "";
			}
		}

		protected string GuessGender(string character) {
			if (character == null) {
				return "unknown";
			}
			string cl = character.ToLower();
			if (cl.IndexOf("female") >= 0) {
				return "female";
			}
			if (cl.StartsWith("male") || cl.IndexOf(" male") >= 0) {
				return "male";
			}
			if (cl == "") {
				return "unknown";
			}
			return "female";
		}

		protected JSONClass SceneInfo() {
			JSONClass info = new JSONClass();
			JSONArray types = new JSONArray();
			JSONArray names = new JSONArray();
			foreach (Atom atom in SuperController.singleton.GetAtoms()) {
				if (atom == null) {
					continue;
				}
				types.Add(atom.type);
				names.Add(atom.uid);
			}
			info["atomTypes"] = types;
			info["atomNames"] = names;
			info["persons"] = ListPersons();
			return info;
		}

		protected JSONArray ListPersons() {
			JSONArray list = new JSONArray();
			foreach (Atom atom in SuperController.singleton.GetAtoms()) {
				if (atom == null || atom.type != "Person") {
					continue;
				}
				JSONClass row = new JSONClass();
				row["uid"] = atom.uid;
				row["name"] = atom.name;
				if (atom.on) {
					row["on"] = "true";
				} else {
					row["on"] = "false";
				}
				string character = PersonCharacter(atom);
				row["character"] = character;
				row["gender"] = GuessGender(character);
				list.Add(row);
			}
			return list;
		}

		protected JSONClass PersonPosition(Atom person) {
			FreeControllerV3 fc = RootControl(person);
			Vector3 pos = fc.transform.position;
			Vector3 rot = fc.transform.eulerAngles;
			JSONClass data = new JSONClass();
			data["person"] = person.uid;
			data["x"] = pos.x.ToString();
			data["y"] = pos.y.ToString();
			data["z"] = pos.z.ToString();
			data["rx"] = rot.x.ToString();
			data["ry"] = rot.y.ToString();
			data["rz"] = rot.z.ToString();
			return data;
		}

		protected JSONClass MovePerson(Atom person, JSONClass cmd) {
			FreeControllerV3 fc = RootControl(person);
			Vector3 pos = fc.transform.position;
			Vector3 rot = fc.transform.eulerAngles;
			if (cmd["x"] != null) {
				pos.x = cmd["x"].AsFloat;
			}
			if (cmd["y"] != null) {
				pos.y = cmd["y"].AsFloat;
			}
			if (cmd["z"] != null) {
				pos.z = cmd["z"].AsFloat;
			}
			if (cmd["dx"] != null) {
				pos.x += cmd["dx"].AsFloat;
			}
			if (cmd["dy"] != null) {
				pos.y += cmd["dy"].AsFloat;
			}
			if (cmd["dz"] != null) {
				pos.z += cmd["dz"].AsFloat;
			}
			if (cmd["rx"] != null) {
				rot.x = cmd["rx"].AsFloat;
			}
			if (cmd["ry"] != null) {
				rot.y = cmd["ry"].AsFloat;
			}
			if (cmd["rz"] != null) {
				rot.z = cmd["rz"].AsFloat;
			}
			fc.transform.position = pos;
			fc.transform.rotation = Quaternion.Euler(rot);
			return PersonPosition(person);
		}

		protected FreeControllerV3 RootControl(Atom person) {
			FreeControllerV3 fc = person.GetStorableByID("control") as FreeControllerV3;
			if (fc == null) {
				throw new Exception("no root control on " + person.uid);
			}
			return fc;
		}

		protected GenerateDAZMorphsControlUI MorphUI(Atom person) {
			JSONStorable geo = person.GetStorableByID("geometry");
			if (geo == null) {
				throw new Exception("no geometry on " + person.uid);
			}
			DAZCharacterSelector selector = geo as DAZCharacterSelector;
			if (selector == null) {
				throw new Exception("geometry is not a character on " + person.uid);
			}
			if (selector.morphsControlUI == null) {
				throw new Exception("no morphsControlUI on " + person.uid);
			}
			return selector.morphsControlUI;
		}

		protected DAZMorph FindMorph(GenerateDAZMorphsControlUI ui, string name) {
			if (ui == null || name == null || name == "") {
				return null;
			}
			DAZMorph morph = ui.GetMorphByDisplayName(name);
			if (morph != null) {
				return morph;
			}
			string want = name.ToLower();
			foreach (string display in ui.GetMorphDisplayNames()) {
				if (display != null && display.ToLower() == want) {
					return ui.GetMorphByDisplayName(display);
				}
			}
			return null;
		}

		protected bool LooksLikeExpression(DAZMorph morph) {
			if (morph == null) {
				return false;
			}
			string name = morph.displayName;
			if (name == null) {
				name = "";
			}
			string region = "";
			string group = "";
			try {
				region = morph.region;
			}
			catch {
			}
			try {
				group = morph.group;
			}
			catch {
			}
			if (region == null) {
				region = "";
			}
			if (group == null) {
				group = "";
			}
			string blob = (name + " | " + region + " | " + group).ToLower();
			if (blob.IndexOf("expression") >= 0) {
				return true;
			}
			string nl = name.ToLower();
			if (nl.IndexOf("eyes rolling") >= 0) {
				return true;
			}
			if (nl.IndexOf("eye rollback") >= 0 || nl.IndexOf("eye roll back") >= 0) {
				return true;
			}
			if (nl.IndexOf("extreme pleasure") >= 0) {
				return true;
			}
			if (name.StartsWith("AA - ")) {
				return true;
			}
			if (nl == "enjoying it" || nl == "taking it" || nl == "mouth resting" || nl == "pouty") {
				return true;
			}
			if (nl.IndexOf("lip bite") >= 0) {
				return true;
			}
			return false;
		}

		protected JSONClass ListExpressions(Atom person) {
			GenerateDAZMorphsControlUI ui = MorphUI(person);
			JSONClass data = new JSONClass();
			data["person"] = person.uid;
			JSONArray items = new JSONArray();
			int count = 0;
			foreach (string display in ui.GetMorphDisplayNames()) {
				DAZMorph morph = ui.GetMorphByDisplayName(display);
				if (!LooksLikeExpression(morph)) {
					continue;
				}
				JSONClass row = new JSONClass();
				row["name"] = display;
				row["value"] = morph.morphValue.ToString();
				try {
					if (morph.region != null) {
						row["region"] = morph.region;
					}
				}
				catch {
				}
				items.Add(row);
				count++;
				if (count >= 200) {
					break;
				}
			}
			data["count"] = count.ToString();
			data["items"] = items;
			return data;
		}

		protected JSONClass SetExpression(Atom person, JSONClass cmd) {
			GenerateDAZMorphsControlUI ui = MorphUI(person);
			bool reset = true;
			if (cmd["reset"] != null) {
				reset = cmd["reset"].AsBool;
			}
			JSONArray cleared = new JSONArray();
			if (reset) {
				foreach (string display in ui.GetMorphDisplayNames()) {
					DAZMorph morph = ui.GetMorphByDisplayName(display);
					if (!LooksLikeExpression(morph)) {
						continue;
					}
					if (morph.morphValue != 0f) {
						morph.morphValue = 0f;
						cleared.Add(display);
					}
				}
			}

			JSONArray applied = new JSONArray();
			JSONArray missing = new JSONArray();
			if (cmd["morphs"] != null && cmd["morphs"].AsArray != null) {
				JSONArray want = cmd["morphs"].AsArray;
				int i;
				for (i = 0; i < want.Count; i++) {
					JSONClass row = want[i].AsObject;
					if (row == null) {
						continue;
					}
					string name = "";
					if (row["name"] != null) {
						name = row["name"].Value;
					}
					if (name == null || name == "") {
						continue;
					}
					float value = 1f;
					if (row["value"] != null) {
						value = row["value"].AsFloat;
					}
					DAZMorph morph = FindMorph(ui, name);
					if (morph == null) {
						missing.Add(name);
						continue;
					}
					morph.morphValue = value;
					JSONClass done = new JSONClass();
					done["name"] = morph.displayName;
					done["value"] = morph.morphValue.ToString();
					applied.Add(done);
				}
			}

			if (applied.Count == 0 && missing.Count > 0 && !reset) {
				throw new Exception("no matching expression morphs on " + person.uid);
			}

			JSONClass data = new JSONClass();
			data["person"] = person.uid;
			if (cmd["expression"] != null) {
				data["expression"] = cmd["expression"].Value;
			}
			data["kind"] = "expression";
			data["applied"] = applied;
			data["missing"] = missing;
			data["cleared"] = cleared;
			if (reset) {
				data["reset"] = "true";
			} else {
				data["reset"] = "false";
			}
			return data;
		}

		protected JSONClass LockHead(Atom person, bool locked) {
			JSONArray changed = new JSONArray();
			string[] controlIds = new string[] { "headControl", "neckControl" };
			int i;
			for (i = 0; i < controlIds.Length; i++) {
				FreeControllerV3 fc = person.GetStorableByID(controlIds[i]) as FreeControllerV3;
				if (fc == null) {
					continue;
				}
				if (locked) {
					fc.currentPositionState = FreeControllerV3.PositionState.On;
					fc.currentRotationState = FreeControllerV3.RotationState.On;
				} else if (controlIds[i] == "neckControl") {
					fc.currentPositionState = FreeControllerV3.PositionState.Off;
					fc.currentRotationState = FreeControllerV3.RotationState.Off;
				}
				changed.Add(controlIds[i]);
			}

			EyesControl eyes = person.GetStorableByID("Eyes") as EyesControl;
			if (eyes != null) {
				if (locked) {
					eyes.currentLookMode = EyesControl.LookMode.Target;
				} else {
					eyes.currentLookMode = EyesControl.LookMode.Player;
				}
				changed.Add("Eyes");
			}

			JSONClass data = new JSONClass();
			data["person"] = person.uid;
			if (locked) {
				data["locked"] = "true";
			} else {
				data["locked"] = "false";
			}
			data["changed"] = changed;
			return data;
		}

		protected void RestorePreset(Atom atom, string path) {
			RestorePreset(atom, path, true);
		}

		protected void RestorePreset(Atom atom, string path, bool honorFileUnlisted) {
			JSONNode node = SuperController.singleton.LoadJSON(path);
			if (node == null) {
				throw new Exception("could not load JSON: " + path);
			}
			JSONClass jc = node.AsObject;
			if (jc == null) {
				throw new Exception("preset is not a JSON object: " + path);
			}
			if (jc["storables"] == null) {
				throw new Exception("preset has no storables array: " + path);
			}
			JSONArray storables = jc["storables"].AsArray;
			if (storables == null) {
				throw new Exception("preset has no storables array: " + path);
			}

			bool setUnlisted = true;
			if (honorFileUnlisted && jc["setUnlistedParamsToDefault"] != null) {
				string flag = jc["setUnlistedParamsToDefault"].Value;
				if (flag == "false" || flag == "False" || flag == "0") {
					setUnlisted = false;
				}
			}
			if (!honorFileUnlisted) {
				setUnlisted = false;
			}

			int restored = 0;
			int i;
			for (i = 0; i < storables.Count; i++) {
				JSONClass storableJSON = storables[i].AsObject;
				if (storableJSON == null) {
					continue;
				}
				string sid = "";
				if (storableJSON["id"] != null) {
					sid = storableJSON["id"].Value;
				}
				if (sid == null || sid == "") {
					continue;
				}
				JSONStorable storable = atom.GetStorableByID(sid);
				if (storable == null) {
					continue;
				}
				storable.RestoreFromJSON(storableJSON, true, true, null, setUnlisted);
				restored++;
			}

			if (restored == 0) {
				throw new Exception("no matching storables on " + atom.uid + " for " + path);
			}
		}

		protected JSONClass StatusPayload() {
			JSONClass data = new JSONClass();
			data["plugin"] = "VamMcpBridge";
			data["version"] = "0.6.0";
			data["vamRoot"] = vamRoot;
			data["bridgeDir"] = bridgeDir;
			if (bridgeEnabled) {
				data["enabled"] = "true";
			} else {
				data["enabled"] = "false";
			}
			data["personCount"] = ListPersons().Count.ToString();
			return data;
		}

		protected void WriteStatusFile(string state, string op) {
			try {
				JSONClass status = StatusPayload();
				status["state"] = state;
				if (op != null && op != "") {
					status["op"] = op;
				}
				SuperController.singleton.SaveJSON(status, statusPath);
			}
			catch {
			}
		}

		protected void SetStatus(string text) {
			if (statusStore != null) {
				statusStore.val = text;
			}
		}

		void OnDestroy() {
			try {
				WriteStatusFile("stopped", "");
			}
			catch {
			}
		}
	}
}
