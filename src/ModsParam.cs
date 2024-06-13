using System.Collections.Generic;
using System.IO;
using System.Xml;
using BepInEx;
using COM3D2.AddModsSlider;

namespace CM3D2.AddModsSlider.Plugin;

internal class ModParameters {
	public readonly string DefMatchPattern = @"([-+]?[0-9]*\.?[0-9]+)";
	public readonly string XmlFileName = Path.Combine(Paths.ConfigPath, "ModsParam.xml");

	private readonly Dictionary<string, Parameter> _parametersDictionary = new();

	public ModParameters() { }

	public List<Parameter> Parameters { get; } = new();

	public bool Init() {
		if (!LoadModParameters()) {
			AddModsSlider.LogError("Failed to load mod parameters.");
			return false;
		}

		ApplyExternalModParameters();

		foreach (var key in Parameters) {
			key.CheckWideSlider();
		}

		return true;
	}

	private Parameter AddParameter(string name, string insertBeforeName = null) {
		var parameter = new Parameter(this, name);
		if (!string.IsNullOrEmpty(insertBeforeName) && _parametersDictionary.TryGetValue(insertBeforeName, out var insertBeforeParameter)) {
			Parameters.Insert(Parameters.IndexOf(insertBeforeParameter), parameter);
		} else {
			Parameters.Add(parameter);
		}
		_parametersDictionary[name] = parameter;
		return parameter;
	}

	public bool HasParameter(string name) => _parametersDictionary.ContainsKey(name);

	private void RemoveParameter(Parameter parameter) {
		Parameters.Remove(parameter);
		_parametersDictionary.Remove(parameter.Name);
	}

	public bool TryGetParameter(string name, out Parameter parameter) => _parametersDictionary.TryGetValue(name, out parameter);
	
	public Parameter GetParameter(string name) => _parametersDictionary[name];

	public bool WideSliderIsEnabled() => TryGetParameter("WIDESLIDER", out var wideSlider) && wideSlider.Enabled;

	private bool LoadModParameters() {
		if (!File.Exists(XmlFileName)) {
			AddModsSlider.LogError($"\"{XmlFileName}\" does not exist.");
			return false;
		}

		var doc = new XmlDocument();
		doc.Load(XmlFileName);

		var mods = (XmlNode)doc.DocumentElement;

		var xmlFormat = ((XmlElement)mods).GetAttribute("format");
		if (xmlFormat != "1.2" && xmlFormat != "1.21") {
			AddModsSlider.LogError($"AddModsSlider v{MyPluginInfo.PLUGIN_VERSION} requires ModsParam.xml format=\"1.2\" or \"1.21\".");
			return false;
		}

		var modNodes = mods.SelectNodes("/mods/mod");
		if (modNodes.Count == 0) {
			AddModsSlider.LogError($"\"{XmlFileName}\" has no <mod> elements.");
			return false;
		}

		Parameters.Clear();
		_parametersDictionary.Clear();

		foreach (XmlElement modNode in modNodes) {
			var key = modNode.GetAttribute("id");

			if (key == "" || HasParameter(key)) {
				continue;
			}

			var parameter = AddParameter(key);
			parameter.Enabled = false;
			parameter.Description = modNode.GetAttribute("description");
			parameter.OnWideSlider = bool.TryParse(modNode.GetAttribute("on_wideslider"), out var onWideSlider) ? onWideSlider : false;
			parameter.Visible = bool.TryParse(modNode.GetAttribute("visible"), out var visible) ? visible : true;

			var type = modNode.GetAttribute("type");

			parameter.Type = type switch {
				"toggle" => type,
				"toggle,slider" => type,
				_ => "slider",
			};

			if (!parameter.IsSlider()) {
				continue;
			}

			var valueNodes = modNode.GetElementsByTagName("value");
			if (valueNodes.Count == 0) {
				continue;
			}

			foreach (XmlElement valueNode in valueNodes) {
				var propName = valueNode.GetAttribute("prop_name");

				if (propName == "" || parameter.HasProperty(propName)) {
					RemoveParameter(parameter);
					break;
				}

				var property = parameter.AddProperty(propName);

				var propertyType = valueNode.GetAttribute("type");
				property.Type = propertyType switch {
					"num" => propertyType,
					"scale" => propertyType,
					"int" => propertyType,
					_ => "num",
				};

				property.MinValue = float.TryParse(valueNode.GetAttribute("min"), out var minValue) ? minValue : 0f;
				property.MaxValue = float.TryParse(valueNode.GetAttribute("max"), out var maxValue) ? maxValue : 0f;

				var defaultValue = float.TryParse(valueNode.GetAttribute("default"), out var @default) ? @default : float.NaN;

				if (float.IsNaN(defaultValue)) {
					defaultValue = property.Type switch {
						"num" => 0f,
						"scale" => 1f,
						"int" => 0f,
						_ => 0f,
					};
				}

				property.DefaultValue = defaultValue;
				property.Value = property.DefaultValue;
				property.Label = valueNode.GetAttribute("label");
				property.MatchPattern = valueNode.GetAttribute("match_pattern");
				property.Visible = bool.TryParse(valueNode.GetAttribute("visible"), out var b) ? b : true;
			}

			if (parameter.PropertyNames.Count == 0) {
				RemoveParameter(parameter);
			}
		}

		return true;
	}

	private void ApplyExternalModParameters() {
		foreach (var modsParam in AddModsSlider.ExternalModParameters) {
			var key = modsParam.sKey;

			if (string.IsNullOrEmpty(key) || HasParameter(key)) {
				return;
			}

			var parameter = AddParameter(key, modsParam.sInsertID);
			parameter.Enabled = false;
			parameter.Description = modsParam.sDescription;
			parameter.OnWideSlider = modsParam.bOnWideSlider;
			parameter.Type = modsParam.sType;
			parameter.Visible = true;

			if (!parameter.IsSlider()) {
				continue;
			}

			if (modsParam.lValueList == null || modsParam.lValueList.Count == 0) {
				RemoveParameter(parameter);
				continue;
			}

			foreach (var modsParamValue in modsParam.lValueList) {
				var propName = modsParamValue.sPropName;

				if (string.IsNullOrEmpty(propName) || parameter.HasProperty(propName)) {
					RemoveParameter(parameter);
					break;
				}

				var property = parameter.AddProperty(propName);
				property.Type = modsParamValue.sType;
				property.MinValue = modsParamValue.fMin;
				property.MaxValue = modsParamValue.fMax;
				property.DefaultValue = modsParamValue.fDef;
				property.Value = modsParamValue.fDef;
				property.Label = modsParamValue.sLabel;
				property.Visible = true;
			}
		}
	}

	internal class Parameter {
		private readonly ModParameters _modParameters;

		public Parameter(ModParameters modParameters, string name) {
			_modParameters = modParameters;
			Name = name;
		}

		public string Name { get; }
		public string Type { get; set; }
		public string Description { get; set; }
		public bool Enabled { get; set; }
		public bool Visible { get; set; }
		public bool OnWideSlider { get; set; }
		public bool WasEnabled { get; set; }
		public List<string> PropertyNames { get; } = new();
		public Dictionary<string, Property> Properties { get; } = new();

		public Property AddProperty(string name) {
			var property = new Property();
			PropertyNames.Add(name);
			Properties.Add(name, property);
			return property;
		}

		public void SetPropertyValue(string name, float value, bool setPrevious = false) {
			var property = Properties[name];
			property.Value = value;
			if (setPrevious) {
				property.PreviousValue = value;
			}
		}

		public void UndoPropertyValue(string name) => Properties[name].UndoValue();

		public void ResetPropertyValue(string name) => Properties[name].ResetValue();

		public bool HasProperty(string name) => Properties.ContainsKey(name);

		internal bool IsToggle() => Type.Contains("toggle");

		internal bool IsSlider() => Type.Contains("slider");

		public bool CheckWideSlider() => !OnWideSlider || _modParameters.WideSliderIsEnabled();

		public class Property {
			public string Type { get; set; }
			public string Label { get; set; }
			public float Value { get; set; }
			public float MinValue { get; set; }
			public float MaxValue { get; set; }
			public float DefaultValue { get; set; }
			public bool Visible { get; set; }
			public string MatchPattern { get; set; }
			public float PreviousValue { get; set; }

			public void UndoValue() => Value = PreviousValue;

			public void ResetValue() => Value = DefaultValue;
		}
	}
}
