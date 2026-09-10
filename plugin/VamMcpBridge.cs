using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using SimpleJSON;
using MVR.FileManagementSecure;
using MVR.Hub;

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
		// Set by an op that answers from a coroutine instead of inline.
		protected bool deferResult;
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

				AdoptStaleCommand();

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

		// The MCP server never deletes command.json, so the last command of the
		// previous session is still on disk when we load. lastCommandId starts
		// empty, so the first PollCommand would replay it. Claim that id without
		// running it: a genuinely new command still has a different id.
		protected void AdoptStaleCommand() {
			try {
				if (!FileManagerSecure.FileExists(commandPath)) {
					return;
				}
				string text = SuperController.singleton.ReadFileIntoString(commandPath);
				if (text == null || text == "") {
					return;
				}
				JSONNode parsed = JSON.Parse(text);
				if (parsed == null) {
					return;
				}
				JSONClass cmd = parsed.AsObject;
				if (cmd == null || cmd["id"] == null) {
					return;
				}
				string id = cmd["id"].Value;
				if (id == null || id == "") {
					return;
				}
				lastCommandId = id;
				SuperController.LogMessage("VamMcpBridge: ignoring stale command " + id);
			}
			catch (Exception e) {
				// Never let a bad leftover file stop the plugin from loading.
				SuperController.LogError("VamMcpBridge.AdoptStaleCommand: " + e);
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
				deferResult = false;
				JSONClass result = Dispatch(cmd);
				result["id"] = id;
				if (result["ok"] == null) {
					result["ok"] = "true";
				}
				if (deferResult) {
					// A coroutine writes result.json when it has the frame.
					SetStatus("pending " + op + " id=" + id);
				} else {
					SuperController.singleton.SaveJSON(result, resultPath);
					SetStatus("ok " + op + " id=" + id);
				}
			}
			catch (Exception e) {
				JSONClass err = new JSONClass();
				err["id"] = id;
				err["ok"] = "false";
				err["op"] = op;
				err["error"] = e.Message;
				SuperController.singleton.SaveJSON(err, resultPath);
				SetStatus("error " + op + ": " + e.Message);
				// Every throw in this file is an op-level failure - a bad argument,
				// or a storable/asset that is not on the atom - and the caller gets
				// it back as ok=false either way. LogError also raises a red toast in
				// VAM's UI, so a routine miss painted the screen with errors: the
				// server asks for DecalMaker's "Clear All Frames" on every character
				// load, and a character without DecalMaker threw twice per load.
				// LogMessage keeps the trace in output_log.txt without the toast.
				SuperController.LogMessage("VamMcpBridge: " + e);
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
				string capId = "";
				if (cmd["id"] != null) {
					capId = cmd["id"].Value;
				}
				deferResult = true;
				SuperController.singleton.StartCoroutine(CaptureCoroutine(capId));
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

			if (op == "list_morphs") {
				Atom person = RequiredPerson(cmd);
				string query = "";
				if (cmd["query"] != null) {
					query = cmd["query"].Value;
				}
				int limit = 60;
				if (cmd["limit"] != null) {
					limit = cmd["limit"].AsInt;
				}
				result["data"] = ListMorphs(person, query, limit);
				return result;
			}

			if (op == "set_morphs") {
				Atom person = RequiredPerson(cmd);
				result["data"] = SetMorphs(person, cmd);
				return result;
			}

			if (op == "list_geometry_options") {
				Atom person = RequiredPerson(cmd);
				string prefix = "";
				if (cmd["prefix"] != null) {
					prefix = cmd["prefix"].Value;
				}
				string query = "";
				if (cmd["query"] != null) {
					query = cmd["query"].Value;
				}
				int limit = 80;
				if (cmd["limit"] != null) {
					limit = cmd["limit"].AsInt;
				}
				result["data"] = GeometryOptions(person, prefix, query, limit);
				return result;
			}

			if (op == "set_geometry_options") {
				Atom person = RequiredPerson(cmd);
				result["data"] = SetGeometryOptions(person, cmd);
				return result;
			}

			if (op == "call_action") {
				Atom person = RequiredPerson(cmd);
				result["data"] = CallAction(person, cmd);
				return result;
			}

			if (op == "set_bool_param") {
				Atom person = RequiredPerson(cmd);
				result["data"] = SetBoolParam(person, cmd);
				return result;
			}

			if (op == "list_actions") {
				Atom person = RequiredPerson(cmd);
				string query = "";
				if (cmd["query"] != null) {
					query = cmd["query"].Value;
				}
				result["data"] = ListActions(person, query);
				return result;
			}

			if (op == "get_appearance") {
				Atom person = RequiredPerson(cmd);
				result["data"] = GetAppearance(person);
				return result;
			}

			if (op == "save_look") {
				Atom person = RequiredPerson(cmd);
				string name = "";
				if (cmd["name"] != null) {
					name = cmd["name"].Value;
				}
				result["data"] = SaveLook(person, name);
				return result;
			}

			if (op == "debug_cameras") {
				result["data"] = DebugCameras();
				return result;
			}

			if (op == "hub_info") {
				result["data"] = HubInfo();
				return result;
			}

			if (op == "hub_status") {
				result["data"] = HubStatus();
				return result;
			}

			if (op == "hub_search") {
				string sid = "";
				if (cmd["id"] != null) {
					sid = cmd["id"].Value;
				}
				deferResult = true;
				SuperController.singleton.StartCoroutine(HubSearchCoroutine(sid, cmd));
				return result;
			}

			if (op == "hub_download") {
				string did = "";
				if (cmd["id"] != null) {
					did = cmd["id"].Value;
				}
				deferResult = true;
				SuperController.singleton.StartCoroutine(HubDownloadCoroutine(did, cmd));
				return result;
			}

			if (op == "list_plugins") {
				Atom person = RequiredPerson(cmd);
				result["data"] = ListPlugins(person);
				return result;
			}

			if (op == "add_plugin") {
				Atom person = RequiredPerson(cmd);
				result["data"] = AddPlugin(person, cmd);
				return result;
			}

			if (op == "rescan_packages") {
				SuperController.singleton.RescanPackages();
				JSONClass data = new JSONClass();
				data["rescanned"] = "true";
				data["note"] = "VAM re-indexes asynchronously; poll list_geometry_options "
					+ "until the new item appears";
				result["data"] = data;
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

		// The character body is not drawn by an enabled Renderer: VAM skins it on
		// the GPU and submits the draw inside the normal render loop. A manual
		// cam.Render() into a RenderTexture runs outside that loop and therefore
		// captures the room and the hair but no person. Grab the real back buffer
		// at the end of a frame instead, which is exactly what the user sees.
		protected IEnumerator CaptureCoroutine(string id) {
			yield return new WaitForEndOfFrame();

			JSONClass result = new JSONClass();
			result["id"] = id;
			result["op"] = "capture_view";
			JSONClass data = new JSONClass();
			Texture2D tex = null;
			try {
				// ScreenCapture lives in UnityEngine.ScreenCaptureModule, which VAM's
				// plugin compiler does not reference. ReadPixels with no active
				// RenderTexture reads the back buffer, which is the same picture.
				RenderTexture.active = null;
				int sw = Screen.width;
				int sh = Screen.height;
				tex = new Texture2D(sw, sh, TextureFormat.RGB24, false);
				tex.ReadPixels(new Rect(0, 0, sw, sh), 0, 0);
				tex.Apply();
				byte[] bytes = tex.EncodeToPNG();
				FileManagerSecure.WriteAllBytes(previewPath, bytes);
				data["path"] = previewPath;
				data["width"] = tex.width.ToString();
				data["height"] = tex.height.ToString();
				data["method"] = "backbuffer";
				result["ok"] = "true";
				result["data"] = data;
			}
			catch (Exception e) {
				// Fall back to the old path so a capture still returns something.
				try {
					data["path"] = CapturePreview();
					data["method"] = "camera-render";
					data["note"] = "back buffer capture failed: " + e.Message;
					result["ok"] = "true";
					result["data"] = data;
				}
				catch (Exception e2) {
					result["ok"] = "false";
					result["error"] = e2.Message;
				}
			}
			if (tex != null) {
				Destroy(tex);
			}
			SuperController.singleton.SaveJSON(result, resultPath);
			SetStatus("ok capture_view id=" + id);
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
				// The param is the "characterSelection" string chooser. The old code
				// asked for a plain string param named "character", which exists under
				// neither that name nor that type, so every person came back "unknown".
				string value = "";
				try {
					value = geo.GetStringChooserParamValue("characterSelection");
				}
				catch {
				}
				if (value == null || value == "") {
					try {
						value = geo.GetStringChooserParamValue("character");
					}
					catch {
					}
				}
				if (value == null || value == "") {
					try {
						value = geo.GetStringParamValue("character");
					}
					catch {
					}
				}
				if (value == null) {
					return "";
				}
				return value;
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
				JSONStorable storable = FindStorable(atom, sid);
				if (storable == null) {
					continue;
				}
				// Plugin slot numbers shift with load order. Rewrite the id so
				// RestoreFromJSON targets the live storable, not plugin#0 from
				// the file. DecalMaker makeup was skipped for this reason.
				if (storable.storeId != null && storable.storeId != sid) {
					storableJSON["id"] = storable.storeId;
				}
				storable.RestoreFromJSON(storableJSON, true, true, null, setUnlisted);
				restored++;
			}

			if (restored == 0) {
				throw new Exception("no matching storables on " + atom.uid + " for " + path);
			}
		}

		// Diagnostic helper: list a storable's string-chooser or plain string
		// params with their current values, so we can see what "character"
		// actually is instead of guessing an accessor.
		protected JSONArray DumpParams(JSONStorable storable, bool choosers) {
			JSONArray rows = new JSONArray();
			try {
				List<string> names;
				if (choosers) {
					names = storable.GetStringChooserParamNames();
				} else {
					names = storable.GetStringParamNames();
				}
				if (names == null) {
					return rows;
				}
				int n = 0;
				foreach (string name in names) {
					if (name == null) {
						continue;
					}
					string value = "";
					try {
						if (choosers) {
							value = storable.GetStringChooserParamValue(name);
						} else {
							value = storable.GetStringParamValue(name);
						}
					}
					catch {
					}
					if (value == null) {
						value = "";
					}
					if (value.Length > 120) {
						value = value.Substring(0, 120) + "...";
					}
					JSONClass row = new JSONClass();
					row["name"] = name;
					row["value"] = value;
					rows.Add(row);
					n++;
					if (n >= 40) {
						break;
					}
				}
			}
			catch {
			}
			return rows;
		}

		protected JSONArray DumpBoolParams(JSONStorable storable) {
			JSONArray rows = new JSONArray();
			try {
				List<string> names = storable.GetBoolParamNames();
				if (names == null) {
					return rows;
				}
				int n = 0;
				foreach (string name in names) {
					if (name == null) {
						continue;
					}
					JSONClass row = new JSONClass();
					row["name"] = name;
					try {
						row["value"] = Bool(storable.GetBoolParamValue(name));
					}
					catch {
						row["value"] = "?";
					}
					rows.Add(row);
					n++;
					if (n >= 60) {
						break;
					}
				}
			}
			catch {
			}
			return rows;
		}

		protected string Bool(bool value) {
			if (value) {
				return "true";
			}
			return "false";
		}

		// Diagnostic. capture_view renders the room and the hair but not the
		// character. Either a camera culls the person's layers, or the skin is not
		// drawn by a Renderer at all (GPU skinning / command buffers), which a
		// manual cam.Render() into a RenderTexture would miss.
		protected JSONClass DebugCameras() {
			JSONClass data = new JSONClass();

			Camera monitor = null;
			try {
				monitor = SuperController.singleton.MonitorCenterCamera;
			}
			catch {
			}
			Camera main = Camera.main;
			if (monitor == null) {
				data["monitorCenterCamera"] = "";
			} else {
				data["monitorCenterCamera"] = monitor.name;
			}
			if (main == null) {
				data["cameraMain"] = "";
			} else {
				data["cameraMain"] = main.name;
			}

			Atom person = null;
			foreach (Atom atom in SuperController.singleton.GetAtoms()) {
				if (atom != null && atom.type == "Person") {
					person = atom;
					break;
				}
			}

			int personMask = 0;
			JSONClass personInfo = new JSONClass();
			JSONArray layerRows = new JSONArray();
			if (person != null) {
				personInfo["uid"] = person.uid;
				Dictionary<int, int> counts = new Dictionary<int, int>();
				Dictionary<int, string> samples = new Dictionary<int, string>();
				Renderer[] rends = person.gameObject.GetComponentsInChildren<Renderer>(true);
				int enabledCount = 0;
				int i;
				for (i = 0; i < rends.Length; i++) {
					Renderer r = rends[i];
					if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) {
						continue;
					}
					enabledCount++;
					int layer = r.gameObject.layer;
					personMask |= (1 << layer);
					if (!counts.ContainsKey(layer)) {
						counts[layer] = 0;
						string shader = "";
						try {
							if (r.sharedMaterial != null && r.sharedMaterial.shader != null) {
								shader = r.sharedMaterial.shader.name;
							}
						}
						catch {
						}
						// r.GetType().Name would reference System.Reflection.MemberInfo,
						// which VAM's plugin sandbox rejects. "is" compiles to isinst.
						string kind = "Renderer";
						if (r is SkinnedMeshRenderer) {
							kind = "SkinnedMeshRenderer";
						} else if (r is MeshRenderer) {
							kind = "MeshRenderer";
						}
						samples[layer] = r.name + " [" + kind + "] " + shader;
					}
					counts[layer] = counts[layer] + 1;
				}
				personInfo["rendererTotal"] = rends.Length.ToString();
				personInfo["rendererEnabled"] = enabledCount.ToString();
				foreach (KeyValuePair<int, int> kv in counts) {
					JSONClass row = new JSONClass();
					row["layer"] = kv.Key.ToString();
					row["layerName"] = LayerMask.LayerToName(kv.Key);
					row["renderers"] = kv.Value.ToString();
					row["sample"] = samples[kv.Key];
					layerRows.Add(row);
				}
			}
			if (person != null) {
				JSONStorable geo = person.GetStorableByID("geometry");
				if (geo != null) {
					personInfo["stringChoosers"] = DumpParams(geo, true);
					personInfo["strings"] = DumpParams(geo, false);
					personInfo["bools"] = DumpBoolParams(geo);
				}
			}
			personInfo["layers"] = layerRows;
			personInfo["layerMaskHex"] = personMask.ToString("X8");
			data["person"] = personInfo;

			JSONArray cams = new JSONArray();
			Camera[] all = Camera.allCameras;
			bool monitorListed = false;
			int c;
			for (c = 0; c < all.Length; c++) {
				Camera cam = all[c];
				if (cam == null) {
					continue;
				}
				if (monitor != null && cam == monitor) {
					monitorListed = true;
				}
				JSONClass row = new JSONClass();
				row["name"] = cam.name;
				row["enabled"] = Bool(cam.enabled);
				row["depth"] = cam.depth.ToString();
				row["cullingMaskHex"] = cam.cullingMask.ToString("X8");
				row["hasTargetTexture"] = Bool(cam.targetTexture != null);
				row["isMonitorCenter"] = Bool(monitor != null && cam == monitor);
				row["isMain"] = Bool(main != null && cam == main);
				row["seesPersonLayers"] = Bool((cam.cullingMask & personMask) != 0);
				row["personLayersCulledHex"] = (personMask & ~cam.cullingMask).ToString("X8");
				cams.Add(row);
			}
			data["cameraCount"] = all.Length.ToString();
			data["monitorInAllCameras"] = Bool(monitorListed);
			data["cameras"] = cams;
			return data;
		}

		protected JSONClass ListMorphs(Atom person, string query, int limit) {
			GenerateDAZMorphsControlUI ui = MorphUI(person);
			string q = "";
			if (query != null) {
				q = query.ToLower();
			}
			if (limit < 1) {
				limit = 1;
			}
			if (limit > 300) {
				limit = 300;
			}
			JSONArray items = new JSONArray();
			int total = 0;
			int matched = 0;
			foreach (string display in ui.GetMorphDisplayNames()) {
				total++;
				if (display == null) {
					continue;
				}
				if (q != "" && display.ToLower().IndexOf(q) < 0) {
					continue;
				}
				matched++;
				if (items.Count >= limit) {
					continue;
				}
				JSONClass row = new JSONClass();
				row["name"] = display;
				DAZMorph morph = ui.GetMorphByDisplayName(display);
				if (morph != null) {
					row["value"] = morph.morphValue.ToString();
					try {
						if (morph.region != null) {
							row["region"] = morph.region;
						}
					}
					catch {
					}
				}
				items.Add(row);
			}
			JSONClass data = new JSONClass();
			data["person"] = person.uid;
			data["total"] = total.ToString();
			data["matched"] = matched.ToString();
			data["returned"] = items.Count.ToString();
			data["items"] = items;
			return data;
		}

		// Like set_expression but for any morph, and it does not clear expressions.
		protected JSONClass SetMorphs(Atom person, JSONClass cmd) {
			GenerateDAZMorphsControlUI ui = MorphUI(person);
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
					float value = 0f;
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
			if (applied.Count == 0 && missing.Count > 0) {
				throw new Exception("no morph matched on " + person.uid + "; check names with list_morphs");
			}
			JSONClass data = new JSONClass();
			data["person"] = person.uid;
			data["applied"] = applied;
			data["missing"] = missing;
			return data;
		}

		// Hair and clothing are bool params on geometry, named "hair:<item>" and
		// "clothing:<item>". List them so a caller can use the real names.
		protected JSONClass GeometryOptions(Atom person, string prefix, string query, int limit) {
			JSONStorable geo = person.GetStorableByID("geometry");
			if (geo == null) {
				throw new Exception("no geometry on " + person.uid);
			}
			string p = "";
			if (prefix != null) {
				p = prefix.ToLower();
			}
			string q = "";
			if (query != null) {
				q = query.ToLower();
			}
			if (limit < 1) {
				limit = 1;
			}
			if (limit > 400) {
				limit = 400;
			}
			JSONArray items = new JSONArray();
			JSONArray active = new JSONArray();
			int matched = 0;
			int total = 0;
			List<string> names = geo.GetBoolParamNames();
			if (names != null) {
				foreach (string name in names) {
					if (name == null) {
						continue;
					}
					total++;
					string lower = name.ToLower();
					if (p != "" && !lower.StartsWith(p)) {
						continue;
					}
					bool on = false;
					try {
						on = geo.GetBoolParamValue(name);
					}
					catch {
					}
					if (on) {
						active.Add(name);
					}
					if (q != "" && lower.IndexOf(q) < 0) {
						continue;
					}
					matched++;
					if (items.Count >= limit) {
						continue;
					}
					JSONClass row = new JSONClass();
					row["name"] = name;
					row["on"] = Bool(on);
					items.Add(row);
				}
			}
			JSONClass data = new JSONClass();
			data["person"] = person.uid;
			data["total"] = total.ToString();
			data["matched"] = matched.ToString();
			data["returned"] = items.Count.ToString();
			data["activeInPrefix"] = active;
			data["items"] = items;
			return data;
		}

		// The usual mistake is a missing "hair:" / "clothing:" prefix, so look for
		// a registered name that ends with what was asked for.
		protected string SuggestOptionName(JSONStorable geo, string wanted) {
			if (wanted == null || wanted == "") {
				return "";
			}
			string low = wanted.ToLower();
			List<string> names = geo.GetBoolParamNames();
			if (names == null) {
				return "";
			}
			for (int i = 0; i < names.Count; i++) {
				string n = names[i];
				if (n != null && n.ToLower().EndsWith(low)) {
					return n;
				}
			}
			for (int i = 0; i < names.Count; i++) {
				string n = names[i];
				if (n != null && n.ToLower().IndexOf(low) >= 0) {
					return n;
				}
			}
			return "";
		}

		// Two packages can ship the same item file name - vamX re-bundles other
		// creators' hair, so "Brows Evey.vam" exists twice. Wearing both stacks
		// the meshes, and the toggle call itself has no way to notice.
		protected JSONArray WornDuplicates(JSONStorable geo) {
			JSONArray dupes = new JSONArray();
			List<string> names = geo.GetBoolParamNames();
			if (names == null) {
				return dupes;
			}
			List<string> leaves = new List<string>();
			List<string> fulls = new List<string>();
			for (int i = 0; i < names.Count; i++) {
				string n = names[i];
				if (n == null || n == "") {
					continue;
				}
				bool on = false;
				try {
					on = geo.GetBoolParamValue(n);
				}
				catch {
					continue;
				}
				if (!on) {
					continue;
				}
				int cut = n.LastIndexOf("/");
				leaves.Add(cut >= 0 ? n.Substring(cut + 1) : n);
				fulls.Add(n);
			}
			List<string> reported = new List<string>();
			for (int i = 0; i < leaves.Count; i++) {
				if (reported.Contains(leaves[i])) {
					continue;
				}
				JSONArray copies = new JSONArray();
				for (int k = 0; k < leaves.Count; k++) {
					if (leaves[k] == leaves[i]) {
						copies.Add(new JSONData(fulls[k]));
					}
				}
				if (copies.Count > 1) {
					reported.Add(leaves[i]);
					JSONClass row = new JSONClass();
					row["item"] = leaves[i];
					row["wornCopies"] = copies;
					dupes.Add(row);
				}
			}
			return dupes;
		}

		protected JSONClass SetGeometryOptions(Atom person, JSONClass cmd) {
			JSONStorable geo = person.GetStorableByID("geometry");
			if (geo == null) {
				throw new Exception("no geometry on " + person.uid);
			}
			// clearPrefix runs first, so "exactly one hair item" is one call.
			JSONArray cleared = new JSONArray();
			string clear = "";
			if (cmd["clearPrefix"] != null) {
				clear = cmd["clearPrefix"].Value;
			}
			if (clear != null && clear != "") {
				string cl = clear.ToLower();
				List<string> names = geo.GetBoolParamNames();
				if (names != null) {
					foreach (string name in names) {
						if (name == null || !name.ToLower().StartsWith(cl)) {
							continue;
						}
						try {
							if (geo.GetBoolParamValue(name)) {
								geo.SetBoolParamValue(name, false);
								cleared.Add(name);
							}
						}
						catch {
						}
					}
				}
			}

			JSONArray applied = new JSONArray();
			JSONArray failed = new JSONArray();
			if (cmd["options"] != null && cmd["options"].AsArray != null) {
				JSONArray want = cmd["options"].AsArray;
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
					bool on = true;
					if (row["on"] != null) {
						on = row["on"].AsBool;
					}
					try {
						// SetBoolParamValue is silent about a name that does not
						// exist, so an unknown option would otherwise report as
						// applied and the item would quietly stay as it was.
						if (!geo.IsBoolJSONParam(name)) {
							JSONClass bad = new JSONClass();
							bad["name"] = name;
							bad["error"] = "no such option";
							string hint = SuggestOptionName(geo, name);
							if (hint != "") {
								bad["didYouMean"] = hint;
							}
							failed.Add(bad);
							continue;
						}
						geo.SetBoolParamValue(name, on);
						bool actual = geo.GetBoolParamValue(name);
						if (actual != on) {
							JSONClass bad = new JSONClass();
							bad["name"] = name;
							bad["error"] = "the toggle did not take";
							bad["wanted"] = Bool(on);
							bad["actual"] = Bool(actual);
							failed.Add(bad);
							continue;
						}
						JSONClass done = new JSONClass();
						done["name"] = name;
						done["on"] = Bool(on);
						applied.Add(done);
					}
					catch (Exception e) {
						JSONClass bad = new JSONClass();
						bad["name"] = name;
						bad["error"] = e.Message;
						failed.Add(bad);
					}
				}
			}
			JSONClass data = new JSONClass();
			data["person"] = person.uid;
			data["cleared"] = cleared;
			data["applied"] = applied;
			data["failed"] = failed;
			JSONArray dupes = WornDuplicates(geo);
			if (dupes.Count > 0) {
				data["duplicates"] = dupes;
				data["warning"] = "the same item file is worn more than once, from "
					+ "different packages - the meshes stack. Switch off all but one, "
					+ "using the full id.";
			}
			return data;
		}

		// Which storables belong in an appearance preset. A whitelist, so plugins,
		// controllers and physics never leak into a saved look.
		protected bool AppearanceStorable(string sid) {
			if (sid == null || sid == "") {
				return false;
			}
			string l = sid.ToLower();
			if (l.IndexOf("control") >= 0 || l.IndexOf("tool") >= 0) {
				return false;
			}
			if (l.StartsWith("plugin")) {
				return false;
			}
			if (l == "geometry" || l == "skin" || l == "eyes" || l == "rescaleobject") {
				return true;
			}
			if (l.IndexOf("material") >= 0) {
				return true;
			}
			if (l.IndexOf("hair") >= 0 || l.IndexOf("clothing") >= 0) {
				return true;
			}
			return false;
		}

		// Read-only counterpart of save_look: hand the appearance JSON back and let
		// the MCP server write the file. The server has plain filesystem access, so
		// this avoids VAM's "plugin wants to save json" prompt, and which storables
		// count as appearance becomes a server-side decision that needs no reload.
		// Controllers and plugins are dropped here because those are pose, not looks.
		// Buttons on a storable are JSONStorableAction, which RestoreFromJSON never
		// touches - plugins commonly mark them isStorable=false. DecalMaker's
		// "Clear All Frames" is one, and without it its layers only ever accumulate.
		protected JSONClass CallAction(Atom person, JSONClass cmd) {
			string sid = "";
			if (cmd["storable"] != null) {
				sid = cmd["storable"].Value;
			}
			string action = "";
			if (cmd["action"] != null) {
				action = cmd["action"].Value;
			}
			if (sid == null || sid == "" || action == null || action == "") {
				throw new Exception("call_action needs storable and action");
			}

			JSONStorable storable = FindStorable(person, sid);
			if (storable == null) {
				throw new Exception("storable not found on " + person.uid + ": " + sid);
			}
			JSONStorableAction target = storable.GetAction(action);
			if (target == null) {
				throw new Exception("no action named " + action + " on " + storable.storeId);
			}
			target.actionCallback();

			JSONClass data = new JSONClass();
			data["person"] = person.uid;
			data["storable"] = storable.storeId;
			data["action"] = action;
			data["called"] = "true";
			return data;
		}

		// JSON RestoreFromJSON can set useFemaleMorphsOnMale without firing the
		// morph-library rebuild. SetBoolParamValue runs the real setter.
		protected JSONClass SetBoolParam(Atom person, JSONClass cmd) {
			string sid = "";
			if (cmd["storable"] != null) {
				sid = cmd["storable"].Value;
			}
			string param = "";
			if (cmd["param"] != null) {
				param = cmd["param"].Value;
			}
			if (sid == null || sid == "" || param == null || param == "") {
				throw new Exception("set_bool_param needs storable and param");
			}
			JSONStorable storable = FindStorable(person, sid);
			if (storable == null) {
				throw new Exception("storable not found on " + person.uid + ": " + sid);
			}
			bool val = false;
			if (cmd["value"] != null) {
				val = cmd["value"].AsBool;
			}
			storable.SetBoolParamValue(param, val);
			JSONClass data = new JSONClass();
			data["person"] = person.uid;
			data["storable"] = storable.storeId;
			data["param"] = param;
			data["value"] = Bool(storable.GetBoolParamValue(param));
			return data;
		}

		// Exact id first, then a suffix match, then plugin#N remapping.
		// A plugin storable is "plugin#<n>_Namespace.Class" and n shifts with
		// load order: DecalMaker saved as plugin#0 is plugin#1 when Life is
		// already in slot 0. EndsWith("plugin#0_...") cannot find that.
		protected JSONStorable FindStorable(Atom person, string sid) {
			if (sid == null || sid == "") {
				return null;
			}
			JSONStorable exact = person.GetStorableByID(sid);
			if (exact != null) {
				return exact;
			}
			string want = sid.ToLower();
			foreach (string candidate in person.GetStorableIDs()) {
				if (candidate == null) {
					continue;
				}
				if (candidate.ToLower().EndsWith(want)) {
					return person.GetStorableByID(candidate);
				}
			}
			int marker = sid.IndexOf("plugin#");
			if (marker < 0) {
				return null;
			}
			int under = sid.IndexOf('_', marker);
			if (under < 0 || under + 1 >= sid.Length) {
				return null;
			}
			string prefix = sid.Substring(0, marker).ToLower();
			string suffix = sid.Substring(under).ToLower();
			foreach (string candidate in person.GetStorableIDs()) {
				if (candidate == null) {
					continue;
				}
				string cl = candidate.ToLower();
				int cmark = cl.IndexOf("plugin#");
				if (cmark < 0) {
					continue;
				}
				int cunder = cl.IndexOf('_', cmark);
				if (cunder < 0) {
					continue;
				}
				if (cl.Substring(0, cmark) == prefix && cl.Substring(cunder) == suffix) {
					return person.GetStorableByID(candidate);
				}
			}
			return null;
		}

		protected JSONClass ListActions(Atom person, string query) {
			string q = "";
			if (query != null) {
				q = query.ToLower();
			}
			JSONArray rows = new JSONArray();
			foreach (string sid in person.GetStorableIDs()) {
				if (sid == null) {
					continue;
				}
				JSONStorable storable = person.GetStorableByID(sid);
				if (storable == null) {
					continue;
				}
				List<string> actions = null;
				try {
					actions = storable.GetActionNames();
				}
				catch {
					continue;
				}
				if (actions == null) {
					continue;
				}
				foreach (string name in actions) {
					if (name == null) {
						continue;
					}
					if (q != "" && sid.ToLower().IndexOf(q) < 0 && name.ToLower().IndexOf(q) < 0) {
						continue;
					}
					JSONClass row = new JSONClass();
					row["storable"] = sid;
					row["action"] = name;
					rows.Add(row);
					if (rows.Count >= 200) {
						break;
					}
				}
				if (rows.Count >= 200) {
					break;
				}
			}
			JSONClass data = new JSONClass();
			data["person"] = person.uid;
			data["count"] = rows.Count.ToString();
			data["actions"] = rows;
			return data;
		}

		protected JSONClass GetAppearance(Atom person) {
			JSONArray storables = new JSONArray();
			JSONArray skipped = new JSONArray();
			foreach (string sid in person.GetStorableIDs()) {
				if (sid == null || sid == "") {
					continue;
				}
				string l = sid.ToLower();
				// Plugin storables are handed over too: some of them hold appearance,
				// DecalMaker's makeup layers being the case in point. Which ones count
				// is the server's call, so adding another one needs no recompile.
				if (l.IndexOf("animation") >= 0) {
					skipped.Add(sid);
					continue;
				}
				JSONStorable st = person.GetStorableByID(sid);
				if (st == null) {
					continue;
				}
				// Pose lives on the skeleton controllers, and those are all
				// FreeControllerV3. Testing the type instead of the name keeps
				// EyelidControl, BreastControl, GluteControl and JawControl, which
				// are appearance despite what they are called - EyelidControl in
				// particular decides whether a narrow eye shape survives a reload.
				if (st is FreeControllerV3) {
					skipped.Add(sid);
					continue;
				}
				try {
					JSONClass js = st.GetJSON();
					if (js == null) {
						continue;
					}
					js["id"] = sid;
					storables.Add(js);
				}
				catch {
				}
			}
			JSONClass data = new JSONClass();
			data["person"] = person.uid;
			data["count"] = storables.Count.ToString();
			data["skippedCount"] = skipped.Count.ToString();
			data["storables"] = storables;
			return data;
		}

		// Superseded by get_appearance: this path makes VAM ask the user to allow a
		// plugin file write. Kept so an older server keeps working.
		protected JSONClass SaveLook(Atom person, string name) {
			string safe = "";
			if (name != null) {
				int i;
				for (i = 0; i < name.Length; i++) {
					char c = name[i];
					bool okChar = char.IsLetterOrDigit(c);
					if (c == 95 || c == 45 || c == 32) {
						okChar = true;
					}
					if (okChar) {
						safe = safe + c;
					}
				}
				safe = safe.Trim();
			}
			if (safe == "") {
				throw new Exception("save_look needs a name of letters, digits, space, _ or -");
			}
			if (safe.Length > 60) {
				safe = safe.Substring(0, 60);
			}

			JSONArray storables = new JSONArray();
			JSONArray kept = new JSONArray();
			foreach (string sid in person.GetStorableIDs()) {
				if (!AppearanceStorable(sid)) {
					continue;
				}
				JSONStorable st = person.GetStorableByID(sid);
				if (st == null) {
					continue;
				}
				try {
					JSONClass js = st.GetJSON();
					if (js == null) {
						continue;
					}
					js["id"] = sid;
					storables.Add(js);
					kept.Add(sid);
				}
				catch {
				}
			}
			if (storables.Count == 0) {
				throw new Exception("nothing appearance-like to save on " + person.uid);
			}

			JSONClass vap = new JSONClass();
			vap["setUnlistedParamsToDefault"] = "true";
			vap["storables"] = storables;

			string dir = "Custom/Atom/Person/Appearance/VamMcp";
			FileManagerSecure.CreateDirectory(dir);
			string rel = dir + "/Preset_" + safe + ".vap";
			SuperController.singleton.SaveJSON(vap, rel);

			JSONClass data = new JSONClass();
			data["person"] = person.uid;
			data["name"] = safe;
			data["path"] = rel;
			data["storables"] = kept;
			return data;
		}

		// ---------------- Hub -------------------------------------------------
		// VAM does the networking. This only drives HubBrowse, which is a
		// JSONStorable with public filter setters, and reads the result cards
		// through HubResourceItemUI.connectedItem - all public, no reflection.

		protected HubBrowse Hub() {
			HubBrowse hb = HubBrowse.singleton;
			if (hb == null) {
				throw new Exception("HubBrowse.singleton is null - this build has no Hub");
			}
			return hb;
		}

		// Result cards are prefab instances parented under the Hub canvas. Walk
		// from the root so a container we cannot name still matches, then fall
		// back to a scene-wide search.
		// VAM keeps old cards and detail panels around instead of destroying them,
		// so anything not currently on screen belongs to a previous page.
		protected HubResourceItemUI[] HubItemUIs() {
			HubBrowse hb = Hub();
			HubResourceItemUI[] found = hb.transform.root.GetComponentsInChildren<HubResourceItemUI>(true);
			if (found == null || found.Length == 0) {
				found = UnityEngine.Object.FindObjectsOfType<HubResourceItemUI>();
			}
			if (found == null) {
				return new HubResourceItemUI[0];
			}
			List<HubResourceItemUI> live = new List<HubResourceItemUI>();
			for (int i = 0; i < found.Length; i++) {
				if (found[i] != null && found[i].gameObject.activeInHierarchy) {
					live.Add(found[i]);
				}
			}
			hubScanHidden = found.Length - live.Count;
			return live.ToArray();
		}

		protected HubResourceItemDetailUI[] HubDetailUIs() {
			HubBrowse hb = Hub();
			HubResourceItemDetailUI[] found = hb.transform.root.GetComponentsInChildren<HubResourceItemDetailUI>(true);
			if (found == null || found.Length == 0) {
				found = UnityEngine.Object.FindObjectsOfType<HubResourceItemDetailUI>();
			}
			if (found == null) {
				return new HubResourceItemDetailUI[0];
			}
			List<HubResourceItemDetailUI> live = new List<HubResourceItemDetailUI>();
			for (int i = 0; i < found.Length; i++) {
				if (found[i] != null && found[i].gameObject.activeInHierarchy) {
					live.Add(found[i]);
				}
			}
			return live.ToArray();
		}

		protected HubBrowseUI HubUI() {
			HubBrowse hb = Hub();
			HubBrowseUI[] found = hb.transform.root.GetComponentsInChildren<HubBrowseUI>(true);
			if (found == null || found.Length == 0) {
				found = UnityEngine.Object.FindObjectsOfType<HubBrowseUI>();
			}
			if (found == null || found.Length == 0) {
				return null;
			}
			return found[0];
		}

		protected string HubResourceCountText() {
			HubBrowseUI ui = HubUI();
			if (ui == null || ui.numResourcesText == null) {
				return "";
			}
			return ui.numResourcesText.text;
		}

		// A cheap fingerprint of the current result page, so a stale page cannot
		// be reported as if it were the answer to the new query.
		protected string HubSignature() {
			try {
				HubResourceItemUI[] uis = HubItemUIs();
				string sig = uis.Length.ToString();
				for (int i = 0; i < uis.Length && i < 5; i++) {
					if (uis[i].connectedItem != null) {
						sig = sig + ":" + uis[i].connectedItem.ResourceId;
					}
				}
				return sig;
			}
			catch {
				return "";
			}
		}

		protected bool HubRefreshing() {
			HubBrowseUI ui = HubUI();
			if (ui == null || ui.refreshIndicator == null) {
				return false;
			}
			return ui.refreshIndicator.activeInHierarchy;
		}

		protected bool Lit(GameObject go) {
			return go != null && go.activeInHierarchy;
		}

		protected JSONClass HubItemJSON(HubResourceItemUI ui) {
			HubResourceItem it = ui.connectedItem;
			if (it == null) {
				return null;
			}
			JSONClass j = new JSONClass();
			j["resourceId"] = it.ResourceId;
			j["title"] = it.Title;
			j["creator"] = it.Creator;
			j["category"] = it.Category;
			j["payType"] = it.PayType;
			j["version"] = it.VersionNumber;
			j["downloads"] = it.DownloadCount.ToString();
			j["rating"] = it.Rating.ToString("0.00");
			j["ratings"] = it.RatingsCount.ToString();
			j["tagLine"] = it.TagLine;
			j["updated"] = it.LastUpdateTimestamp.ToString("yyyy-MM-dd");
			// The indicator objects are the same signals the user reads off a card.
			j["inLibrary"] = Lit(ui.inLibraryIndicator) ? "true" : "false";
			j["hubDownloadable"] = Lit(ui.hubDownloadableIndicator) ? "true" : "false";
			j["hubHosted"] = Lit(ui.hubHostedIndicator) ? "true" : "false";
			j["updateAvailable"] = Lit(ui.updateAvailableIndicator) ? "true" : "false";
			if (ui.dependencyCountText != null) {
				j["dependencies"] = ui.dependencyCountText.text;
			}
			return j;
		}

		// A chooser name is discovered, never guessed: find the registered
		// JSONStorableStringChooser whose id contains the token, then the choice
		// containing the wanted word. Reports the choices when nothing matched.
		protected string SetChooserLike(HubBrowse hb, string idToken, string wantedValue, JSONClass report) {
			List<string> names = hb.GetStringChooserParamNames();
			if (names == null) {
				return null;
			}
			for (int i = 0; i < names.Count; i++) {
				string n = names[i];
				if (n == null || n.ToLower().IndexOf(idToken.ToLower()) < 0) {
					continue;
				}
				List<string> choices = hb.GetStringChooserJSONParamChoices(n);
				if (choices == null) {
					continue;
				}
				string want = wantedValue.ToLower();
				for (int pass = 0; pass < 2; pass++) {
					for (int k = 0; k < choices.Count; k++) {
						string c = choices[k];
						if (c == null) {
							continue;
						}
						string lc = c.ToLower();
						bool hit = pass == 0 ? lc == want : lc.IndexOf(want) >= 0;
						if (hit) {
							hb.SetStringChooserParamValue(n, c);
							report[idToken] = n + " = " + c;
							return c;
						}
					}
				}
				JSONArray arr = new JSONArray();
				for (int k = 0; k < choices.Count && k < 60; k++) {
					arr.Add(new JSONData(choices[k]));
				}
				report[idToken + "Choices"] = arr;
			}
			return null;
		}

		protected JSONArray StringList(List<string> src) {
			JSONArray arr = new JSONArray();
			if (src != null) {
				for (int i = 0; i < src.Count; i++) {
					arr.Add(new JSONData(src[i]));
				}
			}
			return arr;
		}

		protected JSONClass HubInfo() {
			HubBrowse hb = Hub();
			JSONClass data = new JSONClass();
			data["hubEnabled"] = hb.HubEnabled ? "true" : "false";
			data["isShowing"] = hb.IsShowing ? "true" : "false";
			data["itemsOnPage"] = HubItemUIs().Length.ToString();
			HubDownloader hd = HubDownloader.singleton;
			if (hd != null) {
				data["downloaderEnabled"] = hd.HubDownloaderEnabled ? "true" : "false";
			}
			// Dump the registered params so the client never has to guess a name.
			JSONClass choosers = new JSONClass();
			List<string> names = hb.GetStringChooserParamNames();
			if (names != null) {
				for (int i = 0; i < names.Count; i++) {
					List<string> choices = hb.GetStringChooserJSONParamChoices(names[i]);
					JSONArray arr = new JSONArray();
					if (choices != null) {
						for (int k = 0; k < choices.Count && k < 60; k++) {
							arr.Add(new JSONData(choices[k]));
						}
					}
					choosers[names[i]] = arr;
				}
			}
			data["choosers"] = choosers;
			data["actions"] = StringList(hb.GetActionNames());
			data["strings"] = StringList(hb.GetStringParamNames());
			data["bools"] = StringList(hb.GetBoolParamNames());
			return data;
		}

		protected JSONClass HubStatus() {
			HubBrowse hb = Hub();
			JSONClass data = new JSONClass();
			data["isDownloading"] = hb.IsDownloading ? "true" : "false";
			data["downloadCount"] = hb.DownloadCount.ToString();
			HubDownloader hd = HubDownloader.singleton;
			if (hd != null) {
				data["pending"] = hd.PendingResourceDownloads.ToString();
			}
			JSONArray pkgs = new JSONArray();
			HubResourceItemDetailUI[] duis = HubDetailUIs();
			for (int i = 0; i < duis.Length; i++) {
				HubResourcePackageUI[] puis = duis[i].GetComponentsInChildren<HubResourcePackageUI>(true);
				for (int k = 0; k < puis.Length; k++) {
					HubResourcePackage p = puis[k].connectedItem;
					if (p == null) {
						continue;
					}
					JSONClass j = new JSONClass();
					j["name"] = p.Name;
					j["downloading"] = p.IsDownloading ? "true" : "false";
					j["queued"] = p.IsDownloadQueued ? "true" : "false";
					j["needsDownload"] = p.NeedsDownload ? "true" : "false";
					j["error"] = p.HadDownloadError ? "true" : "false";
					pkgs.Add(j);
				}
			}
			data["packages"] = pkgs;
			return data;
		}

		protected void WriteDeferred(string id, string op, JSONClass data, string err) {
			JSONClass result = new JSONClass();
			result["id"] = id;
			result["op"] = op;
			if (err == null) {
				result["ok"] = "true";
				result["data"] = data;
			} else {
				result["ok"] = "false";
				result["error"] = err;
			}
			SuperController.singleton.SaveJSON(result, resultPath);
			deferResult = false;
		}

		// A coroutine cannot use out params, so the settle loop reports through
		// these two fields. Only ever read right after the coroutine returns.
		protected float settleWaited;
		protected int settleCount;
		protected bool settleSawRefresh;
		// How many cached, off-screen cards the last scan skipped.
		protected int hubScanHidden;

		protected int HubCount(bool packages) {
			try {
				if (!packages) {
					return HubItemUIs().Length;
				}
				int n = 0;
				HubResourceItemDetailUI[] duis = HubDetailUIs();
				for (int i = 0; i < duis.Length; i++) {
					n += duis[i].GetComponentsInChildren<HubResourcePackageUI>(true).Length;
				}
				return n;
			}
			catch {
				return -1;
			}
		}

		// Wait out a Hub refresh using VAM's own indicator. Setting a filter also
		// schedules a delayed refresh of VAM's own, so the indicator can light
		// twice; only treat it as finished once it has been dark for a while.
		protected IEnumerator SettleRefresh(float wait) {
			float t = 0f;
			bool sawActive = false;
			int dark = 0;
			while (t < wait) {
				yield return new WaitForSeconds(0.25f);
				t += 0.25f;
				if (HubRefreshing()) {
					sawActive = true;
					dark = 0;
				} else {
					dark++;
					// 2s dark after a refresh, or 5s of nothing happening at all.
					if ((sawActive && dark >= 8) || (!sawActive && dark >= 20)) {
						break;
					}
				}
			}
			settleSawRefresh = sawActive;
			settleCount = HubCount(false);
			settleWaited = t;
		}

		// The package list has no indicator of its own, so settle on its size.
		protected IEnumerator SettleCount(bool packages, float wait) {
			float t = 0f;
			int stable = 0;
			int last = -1;
			while (t < wait) {
				yield return new WaitForSeconds(0.5f);
				t += 0.5f;
				int n = HubCount(packages);
				if (n == last && n > 0) {
					stable++;
					if (stable >= 3) {
						break;
					}
				} else {
					stable = 0;
				}
				last = n;
			}
			settleCount = last;
			settleWaited = t;
		}

		protected IEnumerator HubSearchCoroutine(string id, JSONClass cmd) {
			JSONClass data = new JSONClass();
			JSONClass applied = new JSONClass();
			string err = null;
			float wait = 25f;
			string before = HubSignature();
			try {
				HubBrowse hb = Hub();
				if (!hb.HubEnabled) {
					hb.HubEnabled = true;
					applied["enabledHub"] = "true";
				}
				if (cmd["waitFor"] != null) {
					float.TryParse(cmd["waitFor"].Value, out wait);
				}
				if (cmd["show"] == null || cmd["show"].Value != "false") {
					hb.Show();
				}
				hb.SearchFilter = cmd["query"] != null ? cmd["query"].Value : "";
				applied["search"] = hb.SearchFilter;
				if (cmd["category"] != null && cmd["category"].Value != "") {
					SetChooserLike(hb, "categor", cmd["category"].Value, applied);
				}
				if (cmd["payType"] != null && cmd["payType"].Value != "") {
					SetChooserLike(hb, "paytype", cmd["payType"].Value, applied);
				}
				if (cmd["sort"] != null && cmd["sort"].Value != "") {
					SetChooserLike(hb, "sort", cmd["sort"].Value, applied);
				}
				if (cmd["creator"] != null && cmd["creator"].Value != "") {
					SetChooserLike(hb, "creator", cmd["creator"].Value, applied);
				}
				hb.RefreshResources();
			}
			catch (Exception e) {
				err = e.Message;
			}
			if (err != null) {
				WriteDeferred(id, "hub_search", null, err);
				yield break;
			}

			yield return SuperController.singleton.StartCoroutine(SettleRefresh(wait));

			try {
				int limit = 20;
				if (cmd["limit"] != null) {
					int.TryParse(cmd["limit"].Value, out limit);
				}
				HubResourceItemUI[] uis = HubItemUIs();
				List<JSONClass> rows = new List<JSONClass>();
				for (int i = 0; i < uis.Length; i++) {
					JSONClass j = HubItemJSON(uis[i]);
					if (j != null) {
						rows.Add(j);
					}
				}
				// Sort by downloads so "popular" means popular even if the Hub's
				// own ordering could not be set.
				for (int i = 0; i < rows.Count; i++) {
					int best = i;
					int bestN = 0;
					int.TryParse(rows[i]["downloads"].Value, out bestN);
					for (int k = i + 1; k < rows.Count; k++) {
						int n = 0;
						int.TryParse(rows[k]["downloads"].Value, out n);
						if (n > bestN) {
							best = k;
							bestN = n;
						}
					}
					if (best != i) {
						JSONClass tmp = rows[i];
						rows[i] = rows[best];
						rows[best] = tmp;
					}
				}
				JSONArray arr = new JSONArray();
				for (int i = 0; i < rows.Count && i < limit; i++) {
					arr.Add(rows[i]);
				}
				data["applied"] = applied;
				data["waited"] = settleWaited.ToString("0.0");
				data["matched"] = rows.Count.ToString();
				data["resourceCount"] = HubResourceCountText();
				data["sawRefresh"] = settleSawRefresh ? "true" : "false";
				data["hiddenCardsSkipped"] = hubScanHidden.ToString();
				string after = HubSignature();
				if (after == before && !settleSawRefresh) {
					data["stale"] = "true";
					data["note"] = "the result page never changed and no refresh was seen - these are the previous results, not this query";
				} else {
					data["stale"] = "false";
				}
				data["results"] = arr;
			}
			catch (Exception e2) {
				err = e2.Message;
			}
			WriteDeferred(id, "hub_search", data, err);
		}

		protected IEnumerator HubDownloadCoroutine(string id, JSONClass cmd) {
			JSONClass data = new JSONClass();
			string err = null;
			string resourceId = cmd["resourceId"] != null ? cmd["resourceId"].Value : "";
			bool confirm = cmd["confirm"] != null && cmd["confirm"].Value == "true";
			float wait = 30f;
			if (cmd["waitFor"] != null) {
				float.TryParse(cmd["waitFor"].Value, out wait);
			}
			if (resourceId == "") {
				WriteDeferred(id, "hub_download", null, "missing resourceId");
				yield break;
			}

			// Opening the detail is what makes VAM fetch the package list.
			try {
				HubBrowse hb = Hub();
				hb.Show();
				bool opened = false;
				HubResourceItemUI[] uis = HubItemUIs();
				for (int i = 0; i < uis.Length; i++) {
					HubResourceItem it = uis[i].connectedItem;
					if (it != null && it.ResourceId == resourceId) {
						it.OpenDetail();
						opened = true;
						break;
					}
				}
				if (!opened) {
					hb.OpenDetail(resourceId, true);
				}
			}
			catch (Exception e) {
				err = e.Message;
			}
			if (err != null) {
				WriteDeferred(id, "hub_download", null, err);
				yield break;
			}

			yield return SuperController.singleton.StartCoroutine(SettleCount(true, wait));

			try {
				JSONArray arr = new JSONArray();
				List<string> seenPackages = new List<string>();
				int started = 0;
				long bytes = 0;
				HubResourceItemDetailUI[] duis = HubDetailUIs();
				for (int i = 0; i < duis.Length; i++) {
					HubResourceItemDetail det = duis[i].connectedItem;
					if (det == null || det.ResourceId != resourceId) {
						continue;
					}
					HubResourcePackageUI[] puis = duis[i].GetComponentsInChildren<HubResourcePackageUI>(true);
					for (int k = 0; k < puis.Length; k++) {
						HubResourcePackage p = puis[k].connectedItem;
						if (p == null || p.Name == null || seenPackages.Contains(p.Name)) {
							continue;
						}
						seenPackages.Add(p.Name);
						JSONClass j = new JSONClass();
						j["name"] = p.Name;
						j["creator"] = p.Creator;
						j["license"] = p.LicenseType;
						j["fileSize"] = p.FileSize.ToString();
						j["needsDownload"] = p.NeedsDownload ? "true" : "false";
						j["canBeDownloaded"] = p.CanBeDownloaded ? "true" : "false";
						j["isDependency"] = Lit(puis[k].isDependencyIndicator) ? "true" : "false";
						j["alreadyHave"] = Lit(puis[k].alreadyHaveIndicator) ? "true" : "false";
						j["notOnHub"] = Lit(puis[k].notOnHubIndicator) ? "true" : "false";
						if (confirm && p.CanBeDownloaded && p.NeedsDownload) {
							p.Download();
							started++;
							bytes += p.FileSize;
							j["started"] = "true";
						}
						arr.Add(j);
					}
				}
				if (arr.Count == 0) {
					throw new Exception("no open detail panel for resource " + resourceId
						+ " - the Hub did not load it, so nothing was downloaded");
				}
				data["resourceId"] = resourceId;
				data["waited"] = settleWaited.ToString("0.0");
				data["confirmed"] = confirm ? "true" : "false";
				data["packages"] = arr;
				data["started"] = started.ToString();
				data["startedBytes"] = bytes.ToString();
				if (!confirm) {
					data["note"] = "dry run: pass confirm=true to actually download";
				}
			}
			catch (Exception e3) {
				err = e3.Message;
			}
			WriteDeferred(id, "hub_download", data, err);
		}

		// ---------------- plugins ---------------------------------------------
		// An appearance preset can carry a plugin's saved values but cannot load
		// the plugin itself, so a made-up character arrives bare-faced unless the
		// plugin is already on the atom. These two ops close that gap.

		protected MVRPluginManager PluginManagerOf(Atom person) {
			JSONStorable st = person.GetStorableByID("PluginManager");
			MVRPluginManager pm = st as MVRPluginManager;
			if (pm == null) {
				throw new Exception("no PluginManager on " + person.uid);
			}
			return pm;
		}

		protected JSONClass PluginMap(MVRPluginManager pm) {
			JSONClass jc = pm.GetJSON(true, true, false);
			JSONClass map = new JSONClass();
			if (jc != null && jc["plugins"] != null && jc["plugins"].AsObject != null) {
				JSONClass src = jc["plugins"].AsObject;
				foreach (string key in src.Keys) {
					map[key] = src[key].Value;
				}
			}
			return map;
		}

		protected JSONClass ListPlugins(Atom person) {
			JSONClass data = new JSONClass();
			data["person"] = person.uid;
			data["plugins"] = PluginMap(PluginManagerOf(person));
			return data;
		}

		protected JSONClass AddPlugin(Atom person, JSONClass cmd) {
			string path = "";
			if (cmd["path"] != null) {
				path = cmd["path"].Value;
			}
			if (path == null || path == "") {
				throw new Exception("missing path");
			}

			MVRPluginManager pm = PluginManagerOf(person);
			JSONClass map = PluginMap(pm);

			// Already loaded? Say so instead of stacking a second copy, which
			// would give the atom two of the same storable.
			foreach (string key in map.Keys) {
				if (map[key].Value == path) {
					JSONClass same = new JSONClass();
					same["person"] = person.uid;
					same["path"] = path;
					same["alreadyLoaded"] = "true";
					same["slot"] = key;
					same["plugins"] = map;
					return same;
				}
			}

			// HasKey, not a null check: a missing key can come back as a lazy
			// node rather than null, which would spin here forever.
			int slot = 0;
			while (map.HasKey("plugin#" + slot.ToString())) {
				slot++;
			}
			string newSlot = "plugin#" + slot.ToString();
			map[newSlot] = path;

			JSONClass restore = new JSONClass();
			restore["id"] = "PluginManager";
			restore["plugins"] = map;
			// setMissingToDefault false so the entries already there survive.
			pm.LateRestoreFromJSON(restore, true, true, false);

			JSONClass data = new JSONClass();
			data["person"] = person.uid;
			data["path"] = path;
			data["slot"] = newSlot;
			data["plugins"] = PluginMap(pm);
			data["note"] = "a plugin compiles asynchronously - poll list_plugins or "
				+ "get_appearance until its storable appears before setting its params";
			return data;
		}

		protected JSONClass StatusPayload() {
			JSONClass data = new JSONClass();
			data["plugin"] = "VamMcpBridge";
			data["version"] = "0.10.8";
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
