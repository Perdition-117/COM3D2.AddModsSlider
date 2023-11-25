using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using BepInEx;
using UnityEngine;

namespace CM3D2.AddModsSlider.Plugin;

internal class ModsParam {
	private readonly string LogLabel = AddModsSlider.PluginName + " : ";

	public readonly string DefMatchPattern = @"([-+]?[0-9]*\.?[0-9]+)";
	public readonly string XmlFileName = Path.Combine(Paths.ConfigPath, "ModsParam.xml");


	public string XmlFormat;
	public List<string> sKey = new();

	public Dictionary<string, bool> bEnabled = new();
	public Dictionary<string, string> sDescription = new();
	public Dictionary<string, string> sType = new();
	public Dictionary<string, bool> bOnWideSlider = new();
	public Dictionary<string, bool> bVisible = new();

	public Dictionary<string, string[]> sPropName = new();
	public Dictionary<string, Dictionary<string, float>> fValue = new();
	public Dictionary<string, Dictionary<string, float>> fVmin = new();
	public Dictionary<string, Dictionary<string, float>> fVmax = new();
	public Dictionary<string, Dictionary<string, float>> fVdef = new();
	public Dictionary<string, Dictionary<string, string>> sVType = new();
	public Dictionary<string, Dictionary<string, string>> sLabel = new();
	public Dictionary<string, Dictionary<string, string>> sMatchPattern = new();
	public Dictionary<string, Dictionary<string, bool>> bVVisible = new();

	//--------

	public ModsParam() { }

	public bool Init() {
		if (!LoadModParameters()) {
			Debug.LogError(LogLabel + "LoadModParameters() failed.");
			return false;
		}
		ApplyExternalModParameters();
		foreach (var key in sKey) {
			CheckWideSlider(key);
		}

		return true;
	}

	public bool CheckWideSlider(string key) {
		return !bOnWideSlider[key] || (sKey.Contains("WIDESLIDER") && bEnabled["WIDESLIDER"]);
	}

	public bool IsToggle(string key) {
		return sType[key].Contains("toggle");
	}

	public bool IsSlider(string key) {
		return sType[key].Contains("slider");
	}

	//--------

	private bool LoadModParameters() {
		if (!File.Exists(XmlFileName)) {
			Debug.LogError($"{LogLabel}\"{XmlFileName}\" does not exist.");
			return false;
		}

		var doc = new XmlDocument();
		doc.Load(XmlFileName);

		var mods = (XmlNode)doc.DocumentElement;
		XmlFormat = ((XmlElement)mods).GetAttribute("format");
		if (XmlFormat != "1.2" && XmlFormat != "1.21") {
			Debug.LogError($"{LogLabel}{AddModsSlider.Version} requires format=\"1.2\" or \"1.21\" of ModsParam.xml.");
			return false;
		}

		var modNodes = mods.SelectNodes("/mods/mod");
		if (!(modNodes.Count > 0)) {
			Debug.LogError($"{LogLabel} \"{XmlFileName}\" has no <mod>elements.");
			return false;
		}

		sKey.Clear();

		foreach (XmlElement modNode in modNodes) {
			// mod属性
			var key = modNode.GetAttribute("id");
			if (key != "" && !sKey.Contains(key)) {
				sKey.Add(key);
			} else {
				continue;
			}

			var b = false;
			bEnabled[key] = false;
			sDescription[key] = modNode.GetAttribute("description");
			bOnWideSlider[key] = bool.TryParse(modNode.GetAttribute("on_wideslider"), out b) ? b : false;
			bVisible[key] = bool.TryParse(modNode.GetAttribute("visible"), out b) ? b : true;

			sType[key] = modNode.GetAttribute("type");
			switch (sType[key]) {
				case "toggle": break;
				case "toggle,slider": break;
				default: sType[key] = "slider"; break;
			}

			if (!IsSlider(key)) {
				continue;
			}

			var valueNodes = modNode.GetElementsByTagName("value");
			if (!(valueNodes.Count > 0)) {
				continue;
			}

			sPropName[key] = new string[valueNodes.Count];
			fValue[key] = new();
			fVmin[key] = new();
			fVmax[key] = new();
			fVdef[key] = new();
			sVType[key] = new();
			sLabel[key] = new();
			sMatchPattern[key] = new();
			bVVisible[key] = new();

			// value属性
			var j = 0;
			foreach (XmlElement valueNode in valueNodes) {
				var x = 0f;

				string prop = valueNode.GetAttribute("prop_name");
				if (prop != "" && Array.IndexOf(sPropName[key], prop) < 0) {
					sPropName[key][j] = prop;
				} else {
					sKey.Remove(key);
					break;
				}

				sVType[key][prop] = valueNode.GetAttribute("type");
				switch (sVType[key][prop]) {
					case "num": break;
					case "scale": break;
					case "int": break;
					default: sVType[key][prop] = "num"; break;
				}

				fVmin[key][prop] = float.TryParse(valueNode.GetAttribute("min"), out x) ? x : 0f;
				fVmax[key][prop] = float.TryParse(valueNode.GetAttribute("max"), out x) ? x : 0f;
				fVdef[key][prop] = float.TryParse(valueNode.GetAttribute("default"), out x) ? x : float.NaN;
				if (float.IsNaN(fVdef[key][prop])) {
					fVdef[key][prop] = sVType[key][prop] switch {
						"num" => 0f,
						"scale" => 1f,
						"int" => 0f,
						_ => 0f,
					};
				}

				fValue[key][prop] = fVdef[key][prop];

				sLabel[key][prop] = valueNode.GetAttribute("label");
				sMatchPattern[key][prop] = valueNode.GetAttribute("match_pattern");
				bVVisible[key][prop] = bool.TryParse(valueNode.GetAttribute("visible"), out b) ? b : true;

				j++;
			}
			if (j == 0) {
				sKey.Remove(key);
			}
		}

		return true;
	}

	private void ApplyExternalModParameters() {
		foreach (var modsParam in AddModsSlider.ExternalModParameters) {
			var key = modsParam.sKey;
			if (string.IsNullOrEmpty(key) || sKey.Contains(key)) {
				return;
			}

			if (!string.IsNullOrEmpty(modsParam.sInsertID) && sKey.Contains(modsParam.sInsertID)) {
				sKey.Insert(sKey.IndexOf(modsParam.sInsertID), modsParam.sKey);
			} else {
				sKey.Add(key);
			}
			bEnabled[key] = false;
			sDescription[key] = modsParam.sDescription;
			bOnWideSlider[key] = modsParam.bOnWideSlider;
			sType[key] = modsParam.sType;
			bVisible[key] = true;

			if (!IsSlider(key)) {
				continue;
			}

			if (modsParam.lValueList == null || modsParam.lValueList.Count == 0) {
				sKey.Remove(key);
				continue;
			}
			sPropName[key] = new string[modsParam.lValueList.Count];
			fValue[key] = new();
			fVmin[key] = new();
			fVmax[key] = new();
			fVdef[key] = new();
			sVType[key] = new();
			sLabel[key] = new();
			bVVisible[key] = new();

			var i = 0;
			foreach (var modsParamValue in modsParam.lValueList) {
				var prop = modsParamValue.sPropName;
				if (string.IsNullOrEmpty(prop) || Array.IndexOf(sPropName[key], prop) >= 0) {
					sKey.Remove(key);
					break;
				}

				sPropName[key][i] = prop;
				i++;
				sVType[key][prop] = modsParamValue.sType;
				fVmin[key][prop] = modsParamValue.fMin;
				fVmax[key][prop] = modsParamValue.fMax;
				fVdef[key][prop] = modsParamValue.fDef;
				fValue[key][prop] = modsParamValue.fDef;
				sLabel[key][prop] = modsParamValue.sLabel;
				bVVisible[key][prop] = true;
			}
		}
	}
}
