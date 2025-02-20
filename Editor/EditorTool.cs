using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using Path = System.IO.Path;
using VRC.Udon.Common.Interfaces;
using VRC.Udon.Editor.ProgramSources;
using VRC.Udon.ProgramSources;

namespace UdonFlat {
public class EditorTool {
	[MenuItem("UdonFlat/Generate C# Code", false, 100)]
	public static void GenerateCode() {
		switch(Selection.activeObject) {
		case UdonProgramAsset programAsset:
			GenerateCode(programAsset); break;
		case SerializedUdonProgramAsset serializedProgramAsset:
			GenerateCode(serializedProgramAsset); break;
		default:
			Debug.LogError("Please select a UdonProgramAsset"); break;
		}
	}
	[MenuItem("CONTEXT/UdonProgramAsset/Generate C# Code")]
	static void GenerateCode(MenuCommand command) {
		GenerateCode((UdonProgramAsset)command.context);
	}
	static void GenerateCode(UdonProgramAsset programAsset) {
		var outputPath = Path.Combine("Temp", "UdonFlat", $"{programAsset.name}.cs");
		var program = programAsset.SerializedProgramAsset.RetrieveProgram();
		var timer = new System.Diagnostics.Stopwatch();
		timer.Start();
		GenerateCode(program, outputPath);
		Debug.Log($"[<color=#0c824c>UdonFlat</color>] Code generation finished in {timer.Elapsed.ToString(@"ss\.ff")}");
		UnityEditorInternal.InternalEditorUtility.OpenFileAtLineExternal(outputPath, -1);
	}
	static void GenerateCode(SerializedUdonProgramAsset serializedProgramAsset) {
		var program = serializedProgramAsset.RetrieveProgram();
		var outputPath = Path.Combine("Temp", "UdonFlat", $"udon_{serializedProgramAsset.name}.cs");
		var timer = new System.Diagnostics.Stopwatch();
		timer.Start();
		GenerateCode(program, outputPath);
		Debug.Log($"[<color=#0c824c>UdonFlat</color>] Code generation finished in {timer.Elapsed.ToString(@"ss\.ff")}");
		UnityEditorInternal.InternalEditorUtility.OpenFileAtLineExternal(outputPath, -1);
	}
	static void GenerateCode(IUdonProgram program, string outputPath) {
		System.IO.Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
		var decompiler = new Decompiler{program=program, name=Path.GetFileNameWithoutExtension(outputPath)};
		decompiler.Init();
		decompiler.Translate();
		using(var writer = System.IO.File.CreateText(outputPath))
			decompiler.GenerateCode(writer);
	}
}
}