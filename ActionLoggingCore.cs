using System;
using System.Net;
using System.Linq;
using System.Globalization;
using System.Collections.Generic;

using Network;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using UnityEngine;

using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Core.Libraries;


//
//	This plugin is designed to work with the PvE Rust APIs - https://pverust.com
//


namespace Oxide.Plugins {

	[Info("PvE Rust", "pverust.com", "0.0.1")]
	[Description("The plugin component of the PvE Rust platform.")]

	class ActionLoggingCore : RustPlugin {

		private ConfigData configData;

		string hostname = "https://api.pverust.com";
		string version = "v1";

		string serverSecretKey;

		bool debug;

		List<string> ignoreListOnEntityBuilt = new List<string>();
		List<string> ignoreListOnPickupEntity = new List<string>();
		List<string> ignoreListOnEntityDeath = new List<string>();
		List<string> ignoreListOnLootEntity = new List<string>();
		List<string> ignoreListHandleDoorActions = new List<string>();
		List<string> ignoreListHandleMountableActions = new List<string>();

		public List<object> actionLog;

		bool canSendToPvERust = false;

		private readonly Dictionary<string, string> header = new Dictionary<string, string> {
			["Content-Type"] = "application/json"
		};

		void Init() {

			LoadConfigVariables();

			serverSecretKey = configData.Keys.serverSecretKey;
			
			debug = configData.Services.debug;

			actionLog = new List<object>();

			timer.Repeat(10f, 0, () => {
				SendActionLog();
			});

			VerifyKeys();
			
			InitialiseServer();

		}

		void VerifyKeys() {

			if (serverSecretKey != "") {
				canSendToPvERust = true;
			}

		}

		void InitialiseServer() {

			if (canSendToPvERust) {

				string path = "initialise/fetch.php";
				string endpoint = hostname + "/" + version + "/" + path;

				var data = new {
					serverSecretKey = serverSecretKey
				};

				string payload = JsonConvert.SerializeObject(data);

				webrequest.Enqueue(endpoint, payload, (code, response) => InitialiseServerCallback(code, response), this, RequestMethod.POST, header);

			}

		}

		void InitialiseServerCallback(int code, string response) {

			Puts(response);

			var json = JObject.Parse(response);

			if ((string)json["status"] == "ok") {

				foreach (string prefabName in json["ignoreListOnEntityBuilt"]) {
					ignoreListOnEntityBuilt.Add(prefabName);
				}

				foreach (string prefabName in json["ignoreListOnPickupEntity"]) {
					ignoreListOnPickupEntity.Add(prefabName);
				}

				foreach (string prefabName in json["ignoreListOnEntityDeath"]) {
					ignoreListOnEntityDeath.Add(prefabName);
				}

				foreach (string prefabName in json["ignoreListOnLootEntity"]) {
					ignoreListOnLootEntity.Add(prefabName);
				}

				foreach (string prefabName in json["ignoreListHandleDoorActions"]) {
					ignoreListHandleDoorActions.Add(prefabName);
				}

				foreach (string prefabName in json["ignoreListHandleMountableActions"]) {
					ignoreListHandleMountableActions.Add(prefabName);
				}

			} else if ((string)json["status"] == "error") {

				Puts((string)json["message"]);

			}

		}

		void AppendToLog(string category, BasePlayer initiator, string prefab, string info, string rag) {

			Int32 timestamp = GetTimestamp();

			string initiatorSteamID = initiator.UserIDString;
			string initiatorSteamName = initiator.displayName;

			float positionX = initiator.transform.position.x;
			float positionY = initiator.transform.position.y;
			float positionZ = initiator.transform.position.z;

			actionLog.Add(new {
				timestamp = timestamp,
				initiatorSteamID = initiatorSteamID,
				initiatorSteamName = initiatorSteamName,
				category = category,
				prefab = prefab,
				info = info,
				rag = rag,
				positionX = positionX,
				positionY = positionY,
				positionZ = positionZ
			});

		}

		void SendActionLog() {

			int logCount = actionLog.Count();

			if (logCount != 0) {

				Puts("Send Action Log: " + logCount + " actions");

				string path = "actions/put.php";
				string endpoint = hostname + "/" + version + "/" + path;

				var serializedActionLog = JsonConvert.SerializeObject(actionLog);

Puts("Action log: " + serializedActionLog);

				var data = new {
					serverSecretKey = serverSecretKey,
					actionLog = serializedActionLog
				};

				string payload = JsonConvert.SerializeObject(data);
			
				webrequest.Enqueue(endpoint, payload, (code, response) => SendActionLogCallback(code, response), this, RequestMethod.POST);

			} else {

				Puts("Send Action Log: No actions to send");

			}

		}

		void SendActionLogCallback(int code, string response) {

			if (code == 200) {

				JObject jsonResponse = JObject.Parse(response);

				string status = (string)jsonResponse["status"];

				if (status == "success") {
					
					if (debug) {
						string records = (string)jsonResponse["records"];
						Puts("Status: " + status + " | Records: " + records);
					}

					Puts("Clear action log");
					actionLog.Clear();

				} else if (status == "error") {

					string message = (string)jsonResponse["message"];
					Puts("Status: " + status + " | Message: " + message);

				}

			} else {
				
				Puts("Error Code: " + code);
				Puts("Error Response: " + response);

			}

		}


		// Construction & deployables

		void OnEntityBuilt(Planner plan, GameObject entity) {

			string prefab = entity.ToBaseEntity().ShortPrefabName;

			if (prefab != null) {

				if (ignoreListOnEntityBuilt.Contains(prefab)) {

					BasePlayer initiator = plan.GetOwnerPlayer();

					if (initiator != null) {

						string category = "build";
						string info = "";
						string rag = "green";

						AppendToLog(category, initiator, prefab, info, rag);

					}

				}

			}

		}

		void CanPickupEntity(BasePlayer initiator, BaseCombatEntity entity) {

			string prefab = entity.ShortPrefabName;

			if (prefab != null) {

				if (ignoreListOnPickupEntity.Contains(prefab)) {

					string category = "pickup";
					string info = "";
					string rag = "amber";

					AppendToLog(category, initiator, prefab, info, rag);

				}

			}

		}

		void OnStructureUpgrade(BaseCombatEntity entity, BasePlayer initiator, BuildingGrade.Enum grade) {

			if (entity != null) {

				string prefab = entity.ShortPrefabName;

				if ((prefab != null) && (initiator != null)) {

					string category = "upgrade";
					string info = grade.ToString();
					string rag = "green";

					AppendToLog(category, initiator, prefab, info, rag);

				}

			}

		}

		void OnStructureDemolish(BaseCombatEntity entity, BasePlayer initiator) {

			string prefab = entity.ShortPrefabName;

			if ((prefab != null) && (initiator != null)) {

				string category = "demolish";
				string info = "";
				string rag = "amber";

				AppendToLog(category, initiator, prefab, info, rag);

			}

		}

		void OnEntityDeath(BaseCombatEntity entity, HitInfo player) {

			string prefab = entity.ShortPrefabName;

			if (prefab != null) {
				
				if (ignoreListOnEntityDeath.Contains(prefab)) {

					if ((player != null) && (player.Initiator != null)) {

						BasePlayer initiator = player.Initiator.ToPlayer();

						if (initiator != null) {

							string category = "destroy";
							string info = "";
							string rag = "red";

							AppendToLog(category, initiator, prefab, info, rag);

						}

					}

				}

			}

		}


		// Looting

		void OnLootEntity(BasePlayer initiator, BaseEntity entity) {

			if (entity != null) {

				string category;
				string info;
				string rag;

				string prefab = entity.ShortPrefabName;

				if (ignoreListOnLootEntity.Contains(prefab)) {

					if (entity is BasePlayer) {

						var looted = entity.ToPlayer();

						category = "looted";
						info = looted.displayName;
						rag = "amber";

					} else {

						category = "looted";
						info = "";
						rag = "green";

					}

					AppendToLog(category, initiator, prefab, info, rag);

				}

			}

		}


		// Cupboard auth

		void OnCupboardAuthorize(BuildingPrivlidge privilege, BasePlayer initiator) {

			if (initiator != null) {

				string category = "cupboard";
				string prefab = "cupboard.tool.deployed";
				string info = "authorised";
				string rag = "green";

				AppendToLog(category, initiator, prefab, info, rag);

			}

		}

		void OnCupboardDeauthorize(BuildingPrivlidge privilege, BasePlayer initiator) {

			if (initiator != null) {

				string category = "cupboard";
				string prefab = "cupboard.tool.deployed";
				string info = "deauthorised";
				string rag = "green";

				AppendToLog(category, initiator, prefab, info, rag);

			}

		}

		void OnCupboardClearList(BuildingPrivlidge privilege, BasePlayer initiator) {

			if (initiator != null) {

				string category = "cupboard";
				string prefab = "cupboard.tool.deployed";
				string info = "cleared";
				string rag = "amber";

				AppendToLog(category, initiator, prefab, info, rag);

			}
			
		}


		// Sign usage

		void OnSignUpdated(Signage sign, BasePlayer initiator, string text) {

			string prefab = sign.ShortPrefabName;

			if ((prefab != null) && (initiator != null)) {

				string category = "sign";
				string info = "updated";
				string rag = "green";

				AppendToLog(category, initiator, prefab, info, rag);

			}

		}

		void OnSignLocked(Signage sign, BasePlayer initiator) {

			string prefab = sign.ShortPrefabName;

			if ((prefab != null) && (initiator != null)) {

				string category = "sign";
				string info = "locked";
				string rag = "green";

				AppendToLog(category, initiator, prefab, info, rag);

			}

		}


		// Door usage

		void OnDoorKnocked(Door door, BasePlayer initiator) {

			HandleDoorActions(door, initiator, "knocked");

		}

		void OnDoorOpened(Door door, BasePlayer initiator) {

			HandleDoorActions(door, initiator, "opened");

		}

		void OnDoorClosed(Door door, BasePlayer initiator) {

			HandleDoorActions(door, initiator, "closed");

		}

		void HandleDoorActions(Door door, BasePlayer initiator, string action) {

			string prefab = door.ShortPrefabName;

			if (prefab != null) {

				if (ignoreListHandleDoorActions.Contains(prefab)) {

					if (initiator != null) {

						string category = action;
						string info = action;
						string rag = "green";

						AppendToLog(category, initiator, prefab, info, rag);

					}

				}

			}

		}


		// Mount & dismount

		void OnEntityMounted(BaseMountable mountable, BasePlayer initiator) {

			HandleMountableActions(mountable, initiator, "mounted");

		}

		void OnEntityDismounted(BaseMountable mountable, BasePlayer initiator) {

			HandleMountableActions(mountable, initiator, "dismounted");

		}

		void HandleMountableActions(BaseMountable mountable, BasePlayer initiator, string action) {

			string prefab = mountable.ShortPrefabName;

			if (prefab != null) {
				
				if (ignoreListHandleMountableActions.Contains(prefab)) {

					if (initiator != null) {

						string category = action;
						string info = action;
						string rag = "green";

						AppendToLog(category, initiator, prefab, info, rag);

					}

				}

			}

		}


		// Map wipe

		void OnNewSave(string filename) {

			if (canSendToPvERust) {

				string path = "wipe/put.php";
				string endpoint = hostname + "/" + version + "/" + path;

				var data = new {
					serverSecretKey = serverSecretKey
				};

				string payload = JsonConvert.SerializeObject(data);

				webrequest.Enqueue(endpoint, payload, (code, response) => OnNewSaveCallback(code, response), this, RequestMethod.POST, header);

			}

		}

		void OnNewSaveCallback(int code, string response) {
			
			var json = JObject.Parse(response);

			if ((string)json["status"] == "error") {

				string status = (string)json["status"];
				string message = (string)json["message"];
				Puts("Status: " + status + " | Message: " + message);

			}

		}


		// WebRequests

		void GenericWebRequest(string endpoint, string payload) {

			if (debug) {
				Puts("Endpoint debug: " + endpoint);
				Puts("Payload debug: " + payload);
			}

			if (canSendToPvERust) {
				webrequest.Enqueue(endpoint, payload, (code, response) => GenericCallback(code, response), this, RequestMethod.POST, header);
			}

		}

		void GenericCallback(int code, string response) {

			var json = JObject.Parse(response);

			if ((string)json["status"] == "error") {

				string message = (string)json["message"];
				string endpoint = (string)json["endpoint"];

				Puts("Status: error | Message: " + message + " | Endpoint: " + endpoint);

			}

		}


		// Helpers

		private int GetTimestamp() {

			return (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;

		}

		private string GetFormattedDate() {

			return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

		}

		void DoChat(string chat) {

			rust.BroadcastChat(null, chat);

		}


		// Config management

		private void LoadConfigVariables() {

			configData = Config.ReadObject<ConfigData>();

			// if (configData.Version < new VersionNumber(0, 2, 4)) {
			//	 configData.Store.other4 = 14;
			// }

			configData.Version = Version;

			SaveConfig(configData);

		}

		protected override void LoadDefaultConfig() {

			ConfigData configDefault = new() {

				Keys = new Keys() {
					serverSecretKey = ""
				},
				Services = new Services() {
					debug = false
				},
				Version = Version

			};

			SaveConfig(configDefault);

		}

		private void SaveConfig(ConfigData config) {

			Config.WriteObject(config, true);

		}

		private class ConfigData {

			public Keys Keys;
			public Services Services;
			public VersionNumber Version;

		}
		
		public class Keys {

			public string serverSecretKey;

		}
		
		public class Services {

			public bool debug;

		}

	}

}