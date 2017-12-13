﻿using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;

public class BVHAnimation : ScriptableObject {

	public BVHData Data;
	public Character Character;
	public int[] Symmetry;
	public bool MirrorX, MirrorY, MirrorZ;

	public float UnitScale = 100f;
	public Vector3 PositionOffset = Vector3.zero;
	public Vector3 RotationOffset = Vector3.zero;
	public Vector3[] Corrections;
	
	public BVHFrame[] Frames = new BVHFrame[0];
	public Trajectory Trajectory;
	public BVHPhaseFunction PhaseFunction;
	public BVHPhaseFunction MirroredPhaseFunction;
	public BVHStyleFunction StyleFunction;

	public BVHSequence[] Sequences = new BVHSequence[0];

	public bool ShowMirrored = false;
	public bool ShowPreview = false;
	public bool ShowVelocities = false;
	public bool ShowTrajectory = false;
	public bool ShowZero = false;

	public BVHFrame CurrentFrame = null;
	public int TotalFrames = 0;
	public float TotalTime = 0f;
	public float FrameTime = 0f;
	public float PlayTime = 0f;
	public bool Playing = false;
	public float Timescale = 1f;
	public float TimeWindow = 0f;
	public System.DateTime Timestamp;

	public void EditorUpdate() {
		if(Playing) {
			PlayTime += Timescale*(float)Utility.GetElapsedTime(Timestamp);
			if(PlayTime > TotalTime) {
				PlayTime -= TotalTime;
			}
			LoadFrame(PlayTime);
		}
		Timestamp = Utility.GetTimestamp();

		PhaseFunction.EditorUpdate();
		MirroredPhaseFunction.EditorUpdate();
	}

	public BVHAnimation Create(BVHEditor editor) {
		Load(editor.Path);
		string name = editor.Path.Substring(editor.Path.LastIndexOf("/")+1);
		if(AssetDatabase.LoadAssetAtPath("Assets/Project/"+name+".asset", typeof(BVHAnimation)) == null) {
			AssetDatabase.CreateAsset(this , "Assets/Project/"+name+".asset");
		} else {
			int i = 1;
			while(AssetDatabase.LoadAssetAtPath("Assets/Project/"+name+" ("+i+").asset", typeof(BVHAnimation)) != null) {
				i += 1;
			}
			AssetDatabase.CreateAsset(this, "Assets/Project/"+name+" ("+i+").asset");
		}
		return this;
	}

	private void Load(string path) {
		string[] lines = File.ReadAllLines(path);
		char[] whitespace = new char[] {' '};
		int index = 0;

		//Build Hierarchy
		Data = new BVHData();
		string name = string.Empty;
		string parent = string.Empty;
		Vector3 offset = Vector3.zero;
		int[] channels = null;
		for(index = 0; index<lines.Length; index++) {
			if(lines[index] == "MOTION") {
				break;
			}
			string[] entries = lines[index].Split(whitespace);
			for(int entry=0; entry<entries.Length; entry++) {
				if(entries[entry].Contains("ROOT")) {
					parent = "None";
					name = entries[entry+1];
					break;
				} else if(entries[entry].Contains("JOINT")) {
					parent = name;
					name = entries[entry+1];
					break;
				} else if(entries[entry].Contains("End")) {
					parent = name;
					name = name+entries[entry+1];
					string[] subEntries = lines[index+2].Split(whitespace);
					for(int subEntry=0; subEntry<subEntries.Length; subEntry++) {
						if(subEntries[subEntry].Contains("OFFSET")) {
							offset.x = Utility.ReadFloat(subEntries[subEntry+1]);
							offset.y = Utility.ReadFloat(subEntries[subEntry+2]);
							offset.z = Utility.ReadFloat(subEntries[subEntry+3]);
							break;
						}
					}
					Data.AddBone(name, parent, offset, new int[0]);
					index += 2;
					break;
				} else if(entries[entry].Contains("OFFSET")) {
					offset.x = Utility.ReadFloat(entries[entry+1]);
					offset.y = Utility.ReadFloat(entries[entry+2]);
					offset.z = Utility.ReadFloat(entries[entry+3]);
					break;
				} else if(entries[entry].Contains("CHANNELS")) {
					channels = new int[Utility.ReadInt(entries[entry+1])];
					for(int i=0; i<channels.Length; i++) {
						if(entries[entry+2+i] == "Xposition") {
							channels[i] = 1;
						} else if(entries[entry+2+i] == "Yposition") {
							channels[i] = 2;
						} else if(entries[entry+2+i] == "Zposition") {
							channels[i] = 3;
						} else if(entries[entry+2+i] == "Xrotation") {
							channels[i] = 4;
						} else if(entries[entry+2+i] == "Yrotation") {
							channels[i] = 5;
						} else if(entries[entry+2+i] == "Zrotation") {
							channels[i] = 6;
						}
					}
					Data.AddBone(name, parent, offset, channels);
					break;
				} else if(entries[entry].Contains("}")) {
					name = parent;
					parent = name == "None" ? "None" : Data.FindBone(name).Parent;
					break;
				}
			}
		}

		//Read frame count
		index += 1;
		TotalFrames = Utility.ReadInt(lines[index].Substring(8));

		//Read frame time
		index += 1;
		FrameTime = Utility.ReadFloat(lines[index].Substring(12));

		//Compute total time
		TotalTime = TotalFrames * FrameTime;

		//Read motions
		index += 1;
		for(int i=index; i<lines.Length; i++) {
			Data.AddMotion(Utility.ReadArray(lines[i]));
		}

		//Resize frames
		System.Array.Resize(ref Frames, TotalFrames);

		//Generate character
		GenerateCharacter();

		//Generate frames
		for(int i=0; i<TotalFrames; i++) {
			Frames[i] = new BVHFrame(this, i+1, i*FrameTime);
		}

		//Initialise variables
		TimeWindow = TotalTime;
		PhaseFunction = new BVHPhaseFunction(this);
		MirroredPhaseFunction = new BVHPhaseFunction(this);
		PhaseFunction.SetVelocitySmoothing(0.0f);
		PhaseFunction.SetVelocityThreshold(0.1f);
		PhaseFunction.SetHeightThreshold(0.0f);
		StyleFunction = new BVHStyleFunction(this);
		Sequences = new BVHSequence[0];

		//Generate
		ComputeCorrections();
		ComputeSymmetry();
		ComputeFrames();
		ComputeTrajectory();

		//Load First Frame
		LoadFrame(1);
	}

	public void Recompute() {
		GenerateCharacter();
		ComputeCorrections();
		ComputeSymmetry();
		ComputeFrames();
		ComputeTrajectory();
	}

	public void Reimport(string path) {
		string[] lines = File.ReadAllLines(path);
		char[] whitespace = new char[] {' '};
		int index = 0;

		//Build Hierarchy
		Data = new BVHData();
		string name = string.Empty;
		string parent = string.Empty;
		Vector3 offset = Vector3.zero;
		int[] channels = null;
		for(index = 0; index<lines.Length; index++) {
			if(lines[index] == "MOTION") {
				break;
			}
			string[] entries = lines[index].Split(whitespace);
			for(int entry=0; entry<entries.Length; entry++) {
				if(entries[entry].Contains("ROOT")) {
					parent = "None";
					name = entries[entry+1];
					break;
				} else if(entries[entry].Contains("JOINT")) {
					parent = name;
					name = entries[entry+1];
					break;
				} else if(entries[entry].Contains("End")) {
					parent = name;
					name = name+entries[entry+1];
					string[] subEntries = lines[index+2].Split(whitespace);
					for(int subEntry=0; subEntry<subEntries.Length; subEntry++) {
						if(subEntries[subEntry].Contains("OFFSET")) {
							offset.x = Utility.ReadFloat(subEntries[subEntry+1]);
							offset.y = Utility.ReadFloat(subEntries[subEntry+2]);
							offset.z = Utility.ReadFloat(subEntries[subEntry+3]);
							break;
						}
					}
					Data.AddBone(name, parent, offset, new int[0]);
					index += 2;
					break;
				} else if(entries[entry].Contains("OFFSET")) {
					offset.x = Utility.ReadFloat(entries[entry+1]);
					offset.y = Utility.ReadFloat(entries[entry+2]);
					offset.z = Utility.ReadFloat(entries[entry+3]);
					break;
				} else if(entries[entry].Contains("CHANNELS")) {
					channels = new int[Utility.ReadInt(entries[entry+1])];
					for(int i=0; i<channels.Length; i++) {
						if(entries[entry+2+i] == "Xposition") {
							channels[i] = 1;
						} else if(entries[entry+2+i] == "Yposition") {
							channels[i] = 2;
						} else if(entries[entry+2+i] == "Zposition") {
							channels[i] = 3;
						} else if(entries[entry+2+i] == "Xrotation") {
							channels[i] = 4;
						} else if(entries[entry+2+i] == "Yrotation") {
							channels[i] = 5;
						} else if(entries[entry+2+i] == "Zrotation") {
							channels[i] = 6;
						}
					}
					Data.AddBone(name, parent, offset, channels);
					break;
				} else if(entries[entry].Contains("}")) {
					name = parent;
					parent = name == "None" ? "None" : Data.FindBone(name).Parent;
					break;
				}
			}
		}

		//Read frame count
		index += 1;
		TotalFrames = Utility.ReadInt(lines[index].Substring(8));

		//Read frame time
		index += 1;
		FrameTime = Utility.ReadFloat(lines[index].Substring(12));

		//Compute total time
		TotalTime = TotalFrames * FrameTime;

		//Read motions
		index += 1;
		for(int i=index; i<lines.Length; i++) {
			Data.AddMotion(Utility.ReadArray(lines[i]));
		}

		Recompute();
	}

	public void Play() {
		PlayTime = CurrentFrame.Timestamp;
		Timestamp = Utility.GetTimestamp();
		Playing = true;
	}

	public void Stop() {
		Playing = false;
	}

	public void LoadNextFrame() {
		LoadFrame(Mathf.Min(CurrentFrame.Index+1, TotalFrames));
	}

	public void LoadPreviousFrame() {
		LoadFrame(Mathf.Max(CurrentFrame.Index-1, 1));
	}

	public void LoadFrame(BVHFrame frame) {
		CurrentFrame = frame;
	}

	public void LoadFrame(int index) {
		LoadFrame(GetFrame(index));
	}

	public void LoadFrame(float time) {
		LoadFrame(GetFrame(time));
	}

	public BVHFrame GetFrame(int index) {
		if(index < 1 || index > TotalFrames) {
			Debug.Log("Please specify an index between 1 and " + TotalFrames + ".");
			return null;
		}
		return Frames[index-1];
	}

	public BVHFrame GetFrame(float time) {
		if(time < 0f || time > TotalTime) {
			Debug.Log("Please specify a time between 0 and " + TotalTime + ".");
			return null;
		}
		return GetFrame(Mathf.Min(Mathf.RoundToInt(time / FrameTime) + 1, TotalFrames));
	}

	public BVHFrame[] GetFrames(int startIndex, int endIndex) {
		int count = endIndex-startIndex+1;
		BVHFrame[] frames = new BVHFrame[count];
		int index = 0;
		for(float i=startIndex; i<=endIndex; i++) {
			frames[index] = GetFrame(i);
			index += 1;
		}
		return frames;
	}

	public BVHFrame[] GetFrames(float startTime, float endTime) {
		List<BVHFrame> frames = new List<BVHFrame>();
		for(float t=startTime; t<=endTime; t+=FrameTime) {
			frames.Add(GetFrame(t));
		}
		return frames.ToArray();
	}

	public void GenerateCharacter() {
		Character = new Character();
		string[] names = new string[Data.Bones.Length];
		string[] parents = new string[Data.Bones.Length];
		for(int i=0; i<Data.Bones.Length; i++) {
			names[i] = Data.Bones[i].Name;
			parents[i] = Data.Bones[i].Parent;
		}
		Character.BuildHierarchy(names, parents);
	}

	public void ComputeCorrections() {
		Corrections = new Vector3[Character.Hierarchy.Length];
		//Only for stupid dog bvh...
		for(int i=0; i<Character.Hierarchy.Length; i++) {
			if(	Character.Hierarchy[i].GetName() == "Head" ||
				Character.Hierarchy[i].GetName() == "HeadSite" ||
				Character.Hierarchy[i].GetName() == "LeftShoulder" ||
				Character.Hierarchy[i].GetName() == "RightShoulder"
				) {
				Corrections[i].x = 90f;
				Corrections[i].y = 90f;
				Corrections[i].z = 90f;
			}
			if(Character.Hierarchy[i].GetName() == "Tail") {
				Corrections[i].x = -45f;
			}
		}
		//
	}

	public void ComputeSymmetry() {
		Symmetry = new int[Character.Hierarchy.Length];
		for(int i=0; i<Character.Hierarchy.Length; i++) {
			string name = Character.Hierarchy[i].GetName();
			if(name.Contains("Left")) {
				Character.Segment bone = Character.FindSegment("Right"+name.Substring(4));
				if(bone == null) {
					Debug.Log("Could not find mapping for " + name + ".");
				} else {
					Symmetry[i] = bone.GetIndex();
				}
			} else if(name.Contains("Right")) {
				Character.Segment bone = Character.FindSegment("Left"+name.Substring(5));
				if(bone == null) {
					Debug.Log("Could not find mapping for " + name + ".");
				} else {
					Symmetry[i] = bone.GetIndex();
				}
			} else if(name.StartsWith("L") && char.IsUpper(name[1])) {
				Character.Segment bone = Character.FindSegment("R"+name.Substring(1));
				if(bone == null) {
					Debug.Log("Could not find mapping for " + name + ".");
				} else {
					Symmetry[i] = bone.GetIndex();
				}
			} else if(name.StartsWith("R") && char.IsUpper(name[1])) {
				Character.Segment bone = Character.FindSegment("L"+name.Substring(1));
				if(bone == null) {
					Debug.Log("Could not find mapping for " + name + ".");
				} else {
					Symmetry[i] = bone.GetIndex();
				}
			} else {
				Symmetry[i] = i;
			}
		}
		MirrorX = false;
		MirrorY = false;
		MirrorZ = true;
	}

	public void ComputeFrames() {
		for(int i=0; i<Frames.Length; i++) {
			Frames[i].Generate();
		}
	}

	public void ComputeTrajectory() {
		Trajectory = new Trajectory(TotalFrames, 0);
		LayerMask mask = LayerMask.GetMask("Ground");
		for(int i=0; i<TotalFrames; i++) {
			Vector3 rootPos = Utility.ProjectGround(Frames[i].World[0].GetPosition(), mask);
			//Vector3 rootDir = Frames[i].Rotations[0] * Vector3.forward;
			
			//HARDCODED FOR DOG
			int hipIndex = Character.FindSegment("Hips").GetIndex();
			int neckIndex = Character.FindSegment("Neck").GetIndex();
			Vector3 rootDir = Frames[i].World[neckIndex].GetPosition() - Frames[i].World[hipIndex].GetPosition();
			rootDir.y = 0f;
			rootDir = rootDir.normalized;
			
			Trajectory.Points[i].SetPosition(rootPos);
			Trajectory.Points[i].SetDirection(rootDir);
			Trajectory.Points[i].Postprocess();
		}
	}

	public Matrix4x4[] ExtractZero(bool mirrored) {
		Matrix4x4[] transformations = new Matrix4x4[Character.Hierarchy.Length];
		for(int i=0; i<Character.Hierarchy.Length; i++) {
			BVHData.Bone info = Data.Bones[i];
			Character.Segment parent = Character.Hierarchy[i].GetParent(Character);
			Matrix4x4 local = Matrix4x4.TRS(
				i == 0 ? PositionOffset : info.Offset / UnitScale,
				i == 0 ? Quaternion.Euler(RotationOffset) : Quaternion.identity, 
				Vector3.one
				);
			transformations[i] = parent == null ? local : transformations[parent.GetIndex()] * local;
		}
		for(int i=0; i<Character.Hierarchy.Length; i++) {
			transformations[i] *= Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(Corrections[i]), Vector3.one);
		}
		if(mirrored) {
			for(int i=0; i<Character.Hierarchy.Length; i++) {
				transformations[i] = transformations[i].GetMirror();
			}
		}
		return transformations;
	}

	public Matrix4x4[] ExtractTransformations(BVHFrame frame, bool mirrored) {
		Matrix4x4[] transformations = new Matrix4x4[Character.Hierarchy.Length];
		for(int i=0; i<transformations.Length; i++) {
			transformations[i] = mirrored ? frame.World[Symmetry[i]].GetMirror() : frame.World[i];
		}
		return transformations;
	}

	public Vector3[] ExtractVelocities(BVHFrame frame, bool mirrored, float smoothing=0f) {
		Vector3[] velocities = new Vector3[Character.Hierarchy.Length];
		for(int i=0; i<velocities.Length; i++) {
			velocities[i] = mirrored ? frame.ComputeVelocity(Symmetry[i], smoothing).GetMirror() : frame.ComputeVelocity(i, smoothing);
		}
		return velocities;
	}

	public Trajectory ExtractTrajectory(BVHFrame frame, bool mirrored) {
		Trajectory trajectory = new Trajectory(12, StyleFunction.Styles.Length);
		//Past
		for(int i=0; i<6; i++) {
			float timestamp = Mathf.Clamp(frame.Timestamp - 1f + (float)i/6f, 0f, TotalTime);
			int index = GetFrame(timestamp).Index;
			trajectory.Points[i].SetIndex(Trajectory.Points[index-1].GetIndex());
			trajectory.Points[i].SetPosition(Trajectory.Points[index-1].GetPosition());
			trajectory.Points[i].SetDirection(Trajectory.Points[index-1].GetDirection());
			trajectory.Points[i].SetLeftsample(Trajectory.Points[index-1].GetLeftSample());
			trajectory.Points[i].SetRightSample(Trajectory.Points[index-1].GetRightSample());
			trajectory.Points[i].SetRise(Trajectory.Points[index-1].GetRise());
			for(int j=0; j<StyleFunction.Styles.Length; j++) {
				trajectory.Points[i].Styles[j] = StyleFunction.Styles[j].Values[index-1];
			}
		}
		//Current
		trajectory.Points[6].SetIndex(Trajectory.Points[frame.Index-1].GetIndex());
		trajectory.Points[6].SetPosition(Trajectory.Points[frame.Index-1].GetPosition());
		trajectory.Points[6].SetDirection(Trajectory.Points[frame.Index-1].GetDirection());
		trajectory.Points[6].SetLeftsample(Trajectory.Points[frame.Index-1].GetLeftSample());
		trajectory.Points[6].SetRightSample(Trajectory.Points[frame.Index-1].GetRightSample());
		trajectory.Points[6].SetRise(Trajectory.Points[frame.Index-1].GetRise());
		//Future
		for(int i=7; i<12; i++) {
			float timestamp = Mathf.Clamp(frame.Timestamp + (float)(i-6)/5f, 0f, TotalTime);
			int index = GetFrame(timestamp).Index;
			trajectory.Points[i].SetIndex(Trajectory.Points[index-1].GetIndex());
			trajectory.Points[i].SetPosition(Trajectory.Points[index-1].GetPosition());
			trajectory.Points[i].SetDirection(Trajectory.Points[index-1].GetDirection());
			trajectory.Points[i].SetLeftsample(Trajectory.Points[index-1].GetLeftSample());
			trajectory.Points[i].SetRightSample(Trajectory.Points[index-1].GetRightSample());
			trajectory.Points[i].SetRise(Trajectory.Points[index-1].GetRise());
			for(int j=0; j<StyleFunction.Styles.Length; j++) {
				trajectory.Points[i].Styles[j] = StyleFunction.Styles[j].Values[index-1];
			}
		}

		if(mirrored) {
			for(int i=0; i<12; i++) {
				trajectory.Points[i].SetPosition(trajectory.Points[i].GetPosition().GetMirror());
				trajectory.Points[i].SetDirection(trajectory.Points[i].GetDirection().GetMirror());
				trajectory.Points[i].SetLeftsample(trajectory.Points[i].GetLeftSample().GetMirror());
				trajectory.Points[i].SetRightSample(trajectory.Points[i].GetRightSample().GetMirror());
			}
		}

		return trajectory;
	}

	private void SetUnitScale(float unitScale) {
		if(UnitScale != unitScale) {
			UnitScale = unitScale;
			ComputeFrames();
			ComputeTrajectory();
		}
	}

	private void SetPositionOffset(Vector3 value) {
		if(PositionOffset != value) {
			PositionOffset = value;
			ComputeFrames();
			ComputeTrajectory();
		}
	}

	private void SetRotationOffset(Vector3 value) {
		if(RotationOffset != value) {
			RotationOffset = value;
			ComputeFrames();
			ComputeTrajectory();
		}
	}

	private void SetCorrection(int index, Vector3 correction) {
		if(Corrections[index] != correction) {
			Corrections[index] = correction;
			ComputeFrames();
		}
	}

	private void AddSequence() {
		System.Array.Resize(ref Sequences, Sequences.Length+1);
		Sequences[Sequences.Length-1] = new BVHSequence(this);
	}

	private void RemoveSequence() {
		if(Sequences.Length > 0) {
			System.Array.Resize(ref Sequences, Sequences.Length-1);
		}
	}

	public void Inspector() {
		Utility.SetGUIColor(Utility.DarkGrey);
		using(new EditorGUILayout.VerticalScope ("Box")) {
			Utility.ResetGUIColor();
			EditorGUILayout.BeginHorizontal();
			if(Utility.GUIButton(ShowMirrored ? "Mirrored" : "Default", Utility.Cyan, Utility.Black)) {
				ShowMirrored = !ShowMirrored;
			}
			EditorGUILayout.EndHorizontal();
		}

		Utility.SetGUIColor(Utility.DarkGrey);
		using(new EditorGUILayout.VerticalScope ("Box")) {
			Utility.ResetGUIColor();
			EditorGUILayout.BeginHorizontal();
			if(Utility.GUIButton("Show Velocities", ShowVelocities ? Utility.Green : Utility.Grey, ShowVelocities ? Utility.Black : Utility.LightGrey)) {
				ShowVelocities = !ShowVelocities;
			}
			if(Utility.GUIButton("Show Trajectory", ShowTrajectory ? Utility.Green : Utility.Grey, ShowTrajectory ? Utility.Black : Utility.LightGrey)) {
				ShowTrajectory = !ShowTrajectory;
			}
			if(Utility.GUIButton("Show Preview", ShowPreview ? Utility.Green : Utility.Grey, ShowPreview ? Utility.Black : Utility.LightGrey)) {
				ShowPreview = !ShowPreview;
			}
			if(Utility.GUIButton("Show Zero", ShowZero ? Utility.Green : Utility.Grey, ShowZero ? Utility.Black : Utility.LightGrey)) {
				ShowZero = !ShowZero;
			}
			EditorGUILayout.EndHorizontal();
		}

		if(Utility.GUIButton("Recompute", Utility.Brown, Utility.White)) {
			Recompute();
		}

		if(Utility.GUIButton("Reimport", Utility.Brown, Utility.White)) {
			string path = EditorUtility.OpenFilePanel("BVH Editor", Application.dataPath, "bvh");
			if(name != path.Substring(path.LastIndexOf("/")+1)) {
				Debug.Log("Name mismatch!");
				return;
			} else {
				Debug.Log("Reimported " + name + ".");
			}
			GUI.SetNextControlName("");
			GUI.FocusControl("");
			Reimport(path);
		}

		Utility.SetGUIColor(Utility.LightGrey);
		using(new EditorGUILayout.VerticalScope ("Box")) {
			Utility.ResetGUIColor();
			EditorGUILayout.BeginHorizontal();
			EditorGUILayout.LabelField("Frames: " + TotalFrames, GUILayout.Width(100f));
			EditorGUILayout.LabelField("Time: " + TotalTime.ToString("F3") + "s", GUILayout.Width(100f));
			EditorGUILayout.LabelField("Time/Frame: " + FrameTime.ToString("F3") + "s" + " (" + (1f/FrameTime).ToString("F1") + "Hz)", GUILayout.Width(175f));
			EditorGUILayout.LabelField("Timescale:", GUILayout.Width(65f), GUILayout.Height(20f)); 
			Timescale = EditorGUILayout.FloatField(Timescale, GUILayout.Width(30f), GUILayout.Height(20f));
			EditorGUILayout.EndHorizontal();
		}

		Utility.SetGUIColor(Utility.DarkGrey);
		using(new EditorGUILayout.VerticalScope ("Box")) {
			Utility.ResetGUIColor();

			EditorGUILayout.BeginHorizontal();
			if(Playing) {
				if(Utility.GUIButton("||", Color.red, Color.black, 20f, 20f)) {
					Stop();
				}
			} else {
				if(Utility.GUIButton("|>", Color.green, Color.black, 20f, 20f)) {
					Play();
				}
			}
			if(Utility.GUIButton("<", Utility.Grey, Utility.White, 20f, 20f)) {
				LoadPreviousFrame();
			}
			if(Utility.GUIButton(">", Utility.Grey, Utility.White, 20f, 20f)) {
				LoadNextFrame();
			}
			BVHAnimation.BVHFrame frame = GetFrame(EditorGUILayout.IntSlider(CurrentFrame.Index, 1, TotalFrames, GUILayout.Width(440f)));
			if(CurrentFrame != frame) {
				PlayTime = frame.Timestamp;
				LoadFrame(frame);
			}
			EditorGUILayout.LabelField(CurrentFrame.Timestamp.ToString("F3") + "s", Utility.GetFontColor(Color.white), GUILayout.Width(50f));
			EditorGUILayout.EndHorizontal();
		}

		Utility.SetGUIColor(Utility.DarkGrey);
		using(new EditorGUILayout.VerticalScope ("Box")) {
			Utility.ResetGUIColor();
			
			EditorGUILayout.BeginHorizontal();
			if(Utility.GUIButton("-1s", Utility.Grey, Utility.White, 65, 20f)) {
				LoadFrame(Mathf.Max(CurrentFrame.Timestamp - 1f, 0f));
			}
			TimeWindow = EditorGUILayout.Slider(TimeWindow, 2f*FrameTime, TotalTime, GUILayout.Width(440f));
			if(Utility.GUIButton("+1s", Utility.Grey, Utility.White, 65, 20f)) {
				LoadFrame(Mathf.Min(CurrentFrame.Timestamp + 1f, TotalTime));
			}
			EditorGUILayout.EndHorizontal();
		}

		if(ShowMirrored) {
			MirroredPhaseFunction.Inspector();
		} else {
			PhaseFunction.Inspector();
		}

		StyleFunction.Inspector();


		Utility.SetGUIColor(Utility.DarkGrey);
		using(new EditorGUILayout.VerticalScope ("Box")) {
			Utility.ResetGUIColor();

			Utility.SetGUIColor(Utility.Orange);
			using(new EditorGUILayout.VerticalScope ("Box")) {
				Utility.ResetGUIColor();
				EditorGUILayout.LabelField("Sequences");
			}

			Utility.SetGUIColor(Utility.Grey);
			using(new EditorGUILayout.VerticalScope ("Box")) {
				Utility.ResetGUIColor();

				for(int i=0; i<Sequences.Length; i++) {
					EditorGUILayout.BeginHorizontal();
					EditorGUILayout.LabelField("Start", GUILayout.Width(67f));
					Sequences[i].Start = EditorGUILayout.IntSlider(Sequences[i].Start, 1, TotalFrames, GUILayout.Width(182f));
					EditorGUILayout.LabelField("End", GUILayout.Width(67f));
					Sequences[i].End = EditorGUILayout.IntSlider(Sequences[i].End, 1, TotalFrames, GUILayout.Width(182f));
					EditorGUILayout.LabelField("Export", GUILayout.Width(67f));
					Sequences[i].Export = Mathf.Max(1, EditorGUILayout.IntField(Sequences[i].Export, GUILayout.Width(182f)));
					if(Utility.GUIButton("Auto", Utility.DarkGrey, Utility.White)) {
						Sequences[i].Auto();
					}
					EditorGUILayout.EndHorizontal();
				}

				if(Utility.GUIButton("Add Sequence", Utility.DarkGrey, Utility.White)) {
					AddSequence();
				}
				if(Utility.GUIButton("Remove Sequence", Utility.DarkGrey, Utility.White)) {
					RemoveSequence();
				}
			}
		}

		Utility.SetGUIColor(Utility.DarkGrey);
		using(new EditorGUILayout.VerticalScope ("Box")) {
			Utility.ResetGUIColor();

			Utility.SetGUIColor(Utility.Orange);
			using(new EditorGUILayout.VerticalScope ("Box")) {
				Utility.ResetGUIColor();
				EditorGUILayout.LabelField("Armature");
			}

			Utility.SetGUIColor(Utility.Grey);
			using(new EditorGUILayout.VerticalScope ("Box")) {
				Utility.ResetGUIColor();

				Character.Inspector();

				if(Utility.GUIButton("Export Skeleton", Utility.DarkGrey, Utility.White)) {
					ExportSkeleton();
				}

				SetUnitScale(EditorGUILayout.FloatField("Unit Scale", UnitScale));
				SetPositionOffset(EditorGUILayout.Vector3Field("Position Offset", PositionOffset));
				SetRotationOffset(EditorGUILayout.Vector3Field("Rotation Offset", RotationOffset));

				EditorGUILayout.LabelField("Import Corrections");
				if(Utility.GUIButton("Auto Correct", Utility.DarkGrey, Utility.White)) {
					ComputeCorrections();
				}

				for(int i=0; i<Character.Hierarchy.Length; i++) {
					EditorGUILayout.BeginHorizontal();
					EditorGUI.BeginDisabledGroup(true);
					EditorGUILayout.TextField(Character.Hierarchy[i].GetName());
					EditorGUI.EndDisabledGroup();
					SetCorrection(i, EditorGUILayout.Vector3Field("",Corrections[i]));
					EditorGUILayout.EndHorizontal();
				}
			}
		}
	
		Utility.SetGUIColor(Utility.DarkGrey);
		using(new EditorGUILayout.VerticalScope ("Box")) {
			Utility.ResetGUIColor();

			Utility.SetGUIColor(Utility.Orange);
			using(new EditorGUILayout.VerticalScope ("Box")) {
				Utility.ResetGUIColor();
				EditorGUILayout.LabelField("Symmetry");
			}

			Utility.SetGUIColor(Utility.Grey);
			using(new EditorGUILayout.VerticalScope ("Box")) {
				Utility.ResetGUIColor();
				if(Utility.GUIButton("Compute Symmetry", Utility.DarkGrey, Utility.White)) {
					ComputeSymmetry();
				}
				Utility.SetGUIColor(Utility.DarkGrey);
				using(new EditorGUILayout.VerticalScope ("Box")) {
					Utility.ResetGUIColor();
					EditorGUILayout.BeginHorizontal();
					if(Utility.GUIButton("Mirror X", MirrorX ? Utility.Cyan : Utility.Grey, MirrorX ? Utility.Black : Utility.LightGrey)) {
						MirrorX = !MirrorX;
					}
					if(Utility.GUIButton("Mirror Y", MirrorY ? Utility.Cyan : Utility.Grey, MirrorY ? Utility.Black : Utility.LightGrey)) {
						MirrorY = !MirrorY;
					}
					if(Utility.GUIButton("Mirror Z", MirrorZ ? Utility.Cyan : Utility.Grey, MirrorZ ? Utility.Black : Utility.LightGrey)) {
						MirrorZ = !MirrorZ;
					}
					EditorGUILayout.EndHorizontal();
				}
				string[] names = new string[Character.Hierarchy.Length];
				for(int i=0; i<Character.Hierarchy.Length; i++) {
					names[i] = Character.Hierarchy[i].GetName();
				}
				for(int i=0; i<Character.Hierarchy.Length; i++) {
					EditorGUILayout.BeginHorizontal();
					EditorGUI.BeginDisabledGroup(true);
					EditorGUILayout.TextField(names[i]);
					EditorGUI.EndDisabledGroup();
					Symmetry[i] = EditorGUILayout.Popup(Symmetry[i], names);
					EditorGUILayout.EndHorizontal();
				}
			}
		}
	}

	private void ExportSkeleton() {
		Transform skeleton = ExportSkeleton(Character.GetRoot(), null);
		Transform root = new GameObject("Skeleton").transform;
		root.position = new Vector3(skeleton.position.x, 0f, skeleton.position.z);
		root.rotation = skeleton.rotation;
		skeleton.SetParent(root.transform);

		BioAnimation animation = root.gameObject.AddComponent<BioAnimation>();
		animation.Joints = new Transform[Character.Hierarchy.Length];
		int index = 0;
		AssignJoints(skeleton, ref animation.Joints, ref index);
	}

	private Transform ExportSkeleton(Character.Segment bone, Transform parent) {
		Transform instance = new GameObject(bone.GetName()).transform;
		instance.SetParent(parent);
		instance.position = bone.GetTransformation().GetPosition();
		instance.rotation = bone.GetTransformation().GetRotation();
		for(int i=0; i<bone.GetChildCount(); i++) {
			ExportSkeleton(bone.GetChild(Character, i), instance);
		}
		return instance.root;
	}

	private void AssignJoints(Transform t, ref Transform[] joints, ref int index) {
		joints[index] = t;
		index += 1;
		for(int i=0; i<t.childCount; i++) {
			AssignJoints(t.GetChild(i), ref joints, ref index);
		}
	}

	public void Draw() {
		if(ShowPreview) {
			float step = 1f;
			UnityGL.Start();
			for(int i=1; i<TotalFrames; i++) {
				Matrix4x4[] prevTransformations = ExtractTransformations(Frames[i-1], ShowMirrored);
				Matrix4x4[] currTransformations = ExtractTransformations(Frames[i], ShowMirrored);
				UnityGL.DrawLine(prevTransformations[0].GetPosition(), currTransformations[0].GetPosition(), Utility.Magenta);
			}
			UnityGL.Finish();
			for(float i=0f; i<=TotalTime; i+=step) {
				Matrix4x4[] t = ExtractTransformations(GetFrame(i), ShowMirrored);
				for(int j=0; j<Character.Hierarchy.Length; j++) {
					Character.Hierarchy[j].SetTransformation(t[j]);
				}
				Character.DrawSimple();
			}
		}

		if(ShowTrajectory) {
			Trajectory.Draw();
		} else {
			ExtractTrajectory(CurrentFrame, ShowMirrored).Draw();
		}

		Matrix4x4[] transformations = ShowZero ? ExtractZero(ShowMirrored) : ExtractTransformations(CurrentFrame, ShowMirrored);
		for(int i=0; i<Character.Hierarchy.Length; i++) {
			Character.Hierarchy[i].SetTransformation(transformations[i]);
		}
		Character.Draw();

		UnityGL.Start();
		BVHPhaseFunction function = ShowMirrored ? MirroredPhaseFunction : PhaseFunction;
		for(int i=0; i<function.Variables.Length; i++) {
			if(function.Variables[i]) {
				Color red = Utility.Red;
				red.a = 0.25f;
				Color green = Utility.Green;
				green.a = 0.25f;
				UnityGL.DrawCircle(ShowMirrored ? transformations[Symmetry[i]].GetPosition() : transformations[i].GetPosition(), Character.BoneSize*1.25f, green);
				UnityGL.DrawCircle(ShowMirrored ? transformations[i].GetPosition() : transformations[Symmetry[i]].GetPosition(), Character.BoneSize*1.25f, red);
			}
		}
		UnityGL.Finish();
		
		if(ShowVelocities) {
			Vector3[] velocities = ExtractVelocities(CurrentFrame, ShowMirrored, 0.1f);
			UnityGL.Start();
			for(int i=0; i<Character.Hierarchy.Length; i++) {
				UnityGL.DrawArrow(
					transformations[i].GetPosition(),
					transformations[i].GetPosition() + velocities[i]/FrameTime,
					0.75f,
					0.0075f,
					0.05f,
					new Color(0f, 1f, 1f, 0.5f)
				);				
			}
			UnityGL.Finish();
		}
	}

	[System.Serializable]
	public class BVHSequence {
		public BVHAnimation Animation;
		public int Start = 1;
		public int End = 1;
		public int Export = 1;
		public BVHSequence(BVHAnimation animation) {
			Animation = animation;
			Start = 1;
			End = 1;
			Export = 1;
		}
		public void Auto() {
			int index = System.Array.FindIndex(Animation.Sequences, x => x == this);
			BVHSequence prev = Animation.Sequences[Mathf.Max(0, index-1)];
			BVHSequence next = Animation.Sequences[Mathf.Min(Animation.Sequences.Length-1, index+1)];
			if(prev != this) {
				Start = prev.End + 60;
			} else {
				Start = 61;
			}
			if(next != this) {
				End = next.Start - 60;
			} else {
				End = Animation.TotalFrames-60;
			}
		}
	}

	[System.Serializable]
	public class BVHData {
		public Bone[] Bones;
		public Motion[] Motions;

		public BVHData() {
			Bones = new Bone[0];
			Motions = new Motion[0];
		}

		public void AddBone(string name, string parent, Vector3 offset, int[] channels) {
			System.Array.Resize(ref Bones, Bones.Length+1);
			Bones[Bones.Length-1] = new Bone(name, parent, offset, channels);
		}

		public Bone FindBone(string name) {
			return System.Array.Find(Bones, x => x.Name == name);
		}

		public void AddMotion(float[] values) {
			System.Array.Resize(ref Motions, Motions.Length+1);
			Motions[Motions.Length-1] = new Motion(values);
		}

		[System.Serializable]
		public class Bone {
			public string Name;
			public string Parent;
			public Vector3 Offset;
			public int[] Channels;
			public Bone(string name, string parent, Vector3 offset, int[] channels) {
				Name = name;
				Parent = parent;
				Offset = offset;
				Channels = channels;
			}
		}

		[System.Serializable]
		public class Motion {
			public float[] Values;
			public Motion(float[] values) {
				Values = values;
			}
		}
	}

	[System.Serializable]
	public class BVHFrame {
		public BVHAnimation Animation;

		public int Index;
		public float Timestamp;

		public Matrix4x4[] Local;
		public Matrix4x4[] World;

		public BVHFrame(BVHAnimation animation, int index, float timestamp) {
			Animation = animation;
			Index = index;
			Timestamp = timestamp;

			Local = new Matrix4x4[Animation.Character.Hierarchy.Length];
			World = new Matrix4x4[Animation.Character.Hierarchy.Length];
		}

		public void Generate() {
			int channel = 0;
			BVHData.Motion motion = Animation.Data.Motions[Index-1];
			for(int i=0; i<Animation.Character.Hierarchy.Length; i++) {
				BVHData.Bone info = Animation.Data.Bones[i];
				Vector3 position = Vector3.zero;
				Quaternion rotation = Quaternion.identity;
				for(int j=0; j<info.Channels.Length; j++) {
					if(info.Channels[j] == 1) {
						position.x = motion.Values[channel]; channel += 1;
					}
					if(info.Channels[j] == 2) {
						position.y = motion.Values[channel]; channel += 1;
					}
					if(info.Channels[j] == 3) {
						position.z = motion.Values[channel]; channel += 1;
					}
					if(info.Channels[j] == 4) {
						rotation *= Quaternion.AngleAxis(motion.Values[channel], Vector3.right); channel += 1;
					}
					if(info.Channels[j] == 5) {
						rotation *= Quaternion.AngleAxis(motion.Values[channel], Vector3.up); channel += 1;
					}
					if(info.Channels[j] == 6) {
						rotation *= Quaternion.AngleAxis(motion.Values[channel], Vector3.forward); channel += 1;
					}
				}

				Character.Segment parent = Animation.Character.Hierarchy[i].GetParent(Animation.Character);
				Local[i] = Matrix4x4.TRS(
					i == 0 ? Animation.PositionOffset + Quaternion.Euler(Animation.RotationOffset) * position / Animation.UnitScale : (position+info.Offset) / Animation.UnitScale,
					i == 0 ? Quaternion.Euler(Animation.RotationOffset) * rotation : rotation, 
					Vector3.one
					);
				World[i] = parent == null ? Local[i] : World[parent.GetIndex()] * Local[i];
			}
			for(int i=0; i<Animation.Character.Hierarchy.Length; i++) {
				Local[i] *= Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(Animation.Corrections[i]), Vector3.one);
				World[i] *= Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(Animation.Corrections[i]), Vector3.one);
			}
		}

		public Vector3 ComputeVelocity(int index, float smoothing) {
			if(smoothing == 0f) {
				return World[index].GetPosition() - Animation.GetFrame(Mathf.Max(1, Index-1)).World[index].GetPosition();
			}
			BVHFrame[] frames = Animation.GetFrames(Mathf.Max(0f, Timestamp-smoothing/2f), Mathf.Min(Animation.TotalTime, Timestamp+smoothing/2f));
			Vector3 velocity = Vector3.zero;
			for(int i=1; i<frames.Length; i++) {
				velocity += frames[i].World[index].GetPosition() - frames[i-1].World[index].GetPosition();
			}
			velocity /= frames.Length;
			return velocity;
		}
	}

	[System.Serializable]
	public class BVHPhaseFunction {
		public BVHAnimation Animation;

		public bool[] Keys;
		public float[] Phase;
		public float[] Cycle;
		public float[] NormalisedCycle;
		public bool ShowCycle;

		public Vector2 VariablesScroll;
		public bool[] Variables;
		public float VelocitySmoothing;
		public float VelocityThreshold;
		public float HeightThreshold;

		public float[] Heights;
		public float[] Velocities;
		public float[] NormalisedVelocities;

		private BVHEvolution Optimiser;
		private bool Optimising;

		public BVHPhaseFunction(BVHAnimation animation) {
			Animation = animation;
			Keys = new bool[Animation.TotalFrames];
			Phase = new float[Animation.TotalFrames];
			Cycle = new float[Animation.TotalFrames];
			NormalisedCycle = new float[Animation.TotalFrames];
			Keys[0] = true;
			Keys[Animation.TotalFrames-1] = true;
			Phase[0] = 0f;
			Phase[Animation.TotalFrames-1] = 1f;
			Variables = new bool[Animation.Character.Hierarchy.Length];
			Velocities = new float[Animation.TotalFrames];
			NormalisedVelocities = new float[Animation.TotalFrames];
			Heights = new float[Animation.TotalFrames];
		}

		public void SetKey(BVHFrame frame, bool value) {
			if(value) {
				if(IsKey(frame)) {
					return;
				}
				Keys[frame.Index-1] = true;
				Phase[frame.Index-1] = 1f;
				Interpolate(frame);
			} else {
				if(!IsKey(frame)) {
					return;
				}
				Keys[frame.Index-1] = false;
				Phase[frame.Index-1] = 0f;
				Interpolate(frame);
			}
		}

		public bool IsKey(BVHFrame frame) {
			return Keys[frame.Index-1];
		}

		public void SetPhase(BVHFrame frame, float value) {
			if(Phase[frame.Index-1] != value) {
				Phase[frame.Index-1] = value;
				Interpolate(frame);
			}
		}

		public float GetPhase(BVHFrame frame) {
			return Phase[frame.Index-1];
		}

		public void SetVelocitySmoothing(float value) {
			value = Mathf.Max(0f, value);
			if(VelocitySmoothing != value) {
				Animation.PhaseFunction.VelocitySmoothing = value;
				Animation.MirroredPhaseFunction.VelocitySmoothing = value;
				Animation.PhaseFunction.ComputeValues();
				Animation.MirroredPhaseFunction.ComputeValues();
			}
		}

		public void SetVelocityThreshold(float value) {
			value = Mathf.Max(0f, value);
			if(VelocityThreshold != value) {
				Animation.PhaseFunction.VelocityThreshold = value;
				Animation.MirroredPhaseFunction.VelocityThreshold = value;
				Animation.PhaseFunction.ComputeValues();
				Animation.MirroredPhaseFunction.ComputeValues();
			}
		}

		public void SetHeightThreshold(float value) {
			value = Mathf.Max(0f, value);
			if(HeightThreshold != value) {
				Animation.PhaseFunction.HeightThreshold = value;
				Animation.MirroredPhaseFunction.HeightThreshold = value;
				Animation.PhaseFunction.ComputeValues();
				Animation.MirroredPhaseFunction.ComputeValues();
			}
		}

		public void ToggleVariable(int index) {
			Variables[index] = !Variables[index];
			if(Animation.ShowMirrored) {
				for(int i=0; i<Animation.PhaseFunction.Variables.Length; i++) {
					Animation.PhaseFunction.Variables[Animation.Symmetry[index]] = Variables[index];
				}
			} else {
				for(int i=0; i<Animation.MirroredPhaseFunction.Variables.Length; i++) {
					Animation.MirroredPhaseFunction.Variables[Animation.Symmetry[index]] = Variables[index];
				}
			}
			Animation.PhaseFunction.ComputeValues();
			Animation.MirroredPhaseFunction.ComputeValues();
		}

		public BVHFrame GetPreviousKey(BVHFrame frame) {
			if(frame != null) {
				for(int i=frame.Index-1; i>=1; i--) {
					if(Keys[i-1]) {
						return Animation.Frames[i-1];
					}
				}
			}
			return Animation.Frames[0];
		}

		public BVHFrame GetNextKey(BVHFrame frame) {
			if(frame != null) {
				for(int i=frame.Index+1; i<=Animation.TotalFrames; i++) {
					if(Keys[i-1]) {
						return Animation.Frames[i-1];
					}
				}
			}
			return Animation.Frames[Animation.TotalFrames-1];
		}

		private void Interpolate(BVHFrame frame) {
			if(IsKey(frame)) {
				Interpolate(GetPreviousKey(frame), frame);
				Interpolate(frame, GetNextKey(frame));
			} else {
				Interpolate(GetPreviousKey(frame), GetNextKey(frame));
			}
		}

		private void Interpolate(BVHFrame a, BVHFrame b) {
			if(a == null || b == null) {
				Debug.Log("A given frame was null.");
				return;
			}
			int dist = b.Index - a.Index;
			if(dist >= 2) {
				for(int i=a.Index+1; i<b.Index; i++) {
					float rateA = (float)((float)i-(float)a.Index)/(float)dist;
					float rateB = (float)((float)b.Index-(float)i)/(float)dist;
					Phase[i-1] = rateB*Mathf.Repeat(Phase[a.Index-1], 1f) + rateA*Phase[b.Index-1];
				}
			}

			if(a.Index == 1) {
				BVHFrame first = Animation.Frames[0];
				BVHFrame next1 = GetNextKey(first);
				BVHFrame next2 = GetNextKey(next1);
				Keys[0] = true;
				float xFirst = next1.Timestamp - first.Timestamp;
				float mFirst = next2.Timestamp - next1.Timestamp;
				SetPhase(first, Mathf.Clamp(1f - xFirst / mFirst, 0f, 1f));
			}
			if(b.Index == Animation.TotalFrames) {
				BVHFrame last = Animation.Frames[Animation.TotalFrames-1];
				BVHFrame previous1 = GetPreviousKey(last);
				BVHFrame previous2 = GetPreviousKey(previous1);
				Keys[Animation.TotalFrames-1] = true;
				float xLast = last.Timestamp - previous1.Timestamp;
				float mLast = previous1.Timestamp - previous2.Timestamp;
				SetPhase(last, Mathf.Clamp(xLast / mLast, 0f, 1f));
			}
		}

		private void ComputeValues() {
			for(int i=0; i<Animation.TotalFrames; i++) {
				Heights[i] = 0f;
				Velocities[i] = 0f;
				NormalisedVelocities[i] = 0f;
			}
			float min, max;
			
			LayerMask mask = LayerMask.GetMask("Ground");
			min = float.MaxValue;
			max = float.MinValue;
			for(int i=0; i<Animation.TotalFrames; i++) {
				for(int j=0; j<Animation.Character.Hierarchy.Length; j++) {
					if(Variables[j]) {
						float offset = Mathf.Max(0f, Animation.Frames[i].World[j].GetPosition().y - Utility.ProjectGround(Animation.Frames[i].World[j].GetPosition(), mask).y);
						Heights[i] += offset < HeightThreshold ? 0f : offset;
					}
				}
				if(Heights[i] < min) {
					min = Heights[i];
				}
				if(Heights[i] > max) {
					max = Heights[i];
				}
			}
			for(int i=0; i<Heights.Length; i++) {
				Heights[i] = Utility.Normalise(Heights[i], min, max, 0f, 1f);
			}

			min = float.MaxValue;
			max = float.MinValue;
			for(int i=0; i<Animation.TotalFrames; i++) {
				for(int j=0; j<Animation.Character.Hierarchy.Length; j++) {
					if(Variables[j]) {
						float boneVelocity = Animation.Frames[i].ComputeVelocity(j, VelocitySmoothing).magnitude / Animation.FrameTime;
						Velocities[i] += boneVelocity;
					}
				}
				if(Velocities[i] < VelocityThreshold || Heights[i] == 0f) {
					Velocities[i] = 0f;
				}
				if(Velocities[i] < min) {
					min = Velocities[i];
				}
				if(Velocities[i] > max) {
					max = Velocities[i];
				}
			}
			for(int i=0; i<Velocities.Length; i++) {
				NormalisedVelocities[i] = Utility.Normalise(Velocities[i], min, max, 0f, 1f);
			}
		}

		public void EditorUpdate() {
			if(Optimising) {
				if(Cycle == null) {
					Cycle = new float[Animation.TotalFrames];
				} else if(Cycle.Length != Animation.TotalFrames) {
					Cycle = new float[Animation.TotalFrames];
				}
				if(Optimiser == null) {
					Optimiser = new BVHEvolution(Animation, this);
				}
				Optimiser.Optimise();
			}
		}

		public void Inspector() {
			UnityGL.Start();

			Utility.SetGUIColor(Utility.LightGrey);
			using(new EditorGUILayout.VerticalScope ("Box")) {
				Utility.ResetGUIColor();

				Utility.SetGUIColor(Utility.Orange);
				using(new EditorGUILayout.VerticalScope ("Box")) {
					Utility.ResetGUIColor();
					EditorGUILayout.LabelField("Phase Function");
				}

				Utility.SetGUIColor(Utility.Grey);
				using(new EditorGUILayout.VerticalScope ("Box")) {
					Utility.ResetGUIColor();
					if(Optimising) {
						if(Utility.GUIButton("Stop Optimisation", Utility.LightGrey, Utility.Black)) {
							Optimising = !Optimising;
						}
					} else {
						if(Utility.GUIButton("Start Optimisation", Utility.DarkGrey, Utility.White)) {
							Optimising = !Optimising;
						}
					}
					if(Optimiser != null) {
						if(Utility.GUIButton("Restart", Utility.Brown, Utility.White)) {
							Optimiser.Initialise();
						}
						EditorGUILayout.BeginHorizontal();
						EditorGUILayout.LabelField("Fitness: " + Optimiser.GetFitness(), GUILayout.Width(150f));
						float[] configuration = Optimiser.GetPeakConfiguration();
						EditorGUILayout.LabelField("Peak: " + configuration[0] + " | " + configuration[1] + " | " + configuration[2] + " | " + configuration[3] + " | " + configuration[4]);
						EditorGUILayout.EndHorizontal();
						EditorGUILayout.BeginHorizontal();
						EditorGUILayout.LabelField("Exploration", GUILayout.Width(100f));
						GUILayout.FlexibleSpace();
						Optimiser.Behaviour = EditorGUILayout.Slider(Optimiser.Behaviour, 0f, 1f);
						GUILayout.FlexibleSpace();
						EditorGUILayout.LabelField("Exploitation", GUILayout.Width(100f));
						EditorGUILayout.EndHorizontal();
						Optimiser.SetAmplitude(EditorGUILayout.Slider("Amplitude", Optimiser.Amplitude, 0, BVHEvolution.AMPLITUDE));
						Optimiser.SetFrequency(EditorGUILayout.Slider("Frequency", Optimiser.Frequency, 0f, BVHEvolution.FREQUENCY));
						Optimiser.SetShift(EditorGUILayout.Slider("Shift", Optimiser.Shift, 0, BVHEvolution.SHIFT));
						Optimiser.SetOffset(EditorGUILayout.Slider("Offset", Optimiser.Offset, 0, BVHEvolution.OFFSET));
						Optimiser.SetSlope(EditorGUILayout.Slider("Slope", Optimiser.Slope, 0, BVHEvolution.SLOPE));
						Optimiser.SetWindow(EditorGUILayout.Slider("Window", Optimiser.Window, 0.1f, BVHEvolution.WINDOW));
						Optimiser.Blending = EditorGUILayout.Slider("Blending", Optimiser.Blending, 0f, 1f);
					} else {
						EditorGUILayout.LabelField("No optimiser available.");
					}
				}

				VariablesScroll = EditorGUILayout.BeginScrollView(VariablesScroll, GUILayout.Height(100f));
				for(int i=0; i<Animation.Character.Hierarchy.Length; i++) {
					if(Variables[i]) {
						if(Utility.GUIButton(Animation.Character.Hierarchy[i].GetName(), Utility.DarkGreen, Utility.White)) {
							ToggleVariable(i);
						}
					} else {
						if(Utility.GUIButton(Animation.Character.Hierarchy[i].GetName(), Utility.DarkRed, Utility.White)) {
							ToggleVariable(i);
						}
					}
				}
				EditorGUILayout.EndScrollView();

				SetVelocitySmoothing(EditorGUILayout.FloatField("Velocity Smoothing", VelocitySmoothing));
				SetVelocityThreshold(EditorGUILayout.FloatField("Velocity Threshold", VelocityThreshold));
				SetHeightThreshold(EditorGUILayout.FloatField("Height Threshold", HeightThreshold));

				if(IsKey(Animation.CurrentFrame)) {
					SetPhase(Animation.CurrentFrame, EditorGUILayout.Slider("Phase", GetPhase(Animation.CurrentFrame), 0f, 1f));
				} else {
					EditorGUI.BeginDisabledGroup(true);
					SetPhase(Animation.CurrentFrame, EditorGUILayout.Slider("Phase", GetPhase(Animation.CurrentFrame), 0f, 1f));
					EditorGUI.EndDisabledGroup();
				}

				ShowCycle = EditorGUILayout.Toggle("Show Cycle", ShowCycle);

				if(IsKey(Animation.CurrentFrame)) {
					if(Utility.GUIButton("Unset Key", Utility.Grey, Utility.White)) {
						SetKey(Animation.CurrentFrame, false);
					}
				} else {
					if(Utility.GUIButton("Set Key", Utility.DarkGrey, Utility.White)) {
						SetKey(Animation.CurrentFrame, true);
					}
				}

				EditorGUILayout.BeginHorizontal();
				if(Utility.GUIButton("<", Utility.DarkGrey, Utility.White, 25f, 50f)) {
					Animation.LoadFrame(GetPreviousKey(Animation.CurrentFrame));
				}

				EditorGUILayout.BeginVertical(GUILayout.Height(50f));
				Rect ctrl = EditorGUILayout.GetControlRect();
				Rect rect = new Rect(ctrl.x, ctrl.y, ctrl.width, 50f);
				EditorGUI.DrawRect(rect, Utility.Black);

				float startTime = Animation.CurrentFrame.Timestamp-Animation.TimeWindow/2f;
				float endTime = Animation.CurrentFrame.Timestamp+Animation.TimeWindow/2f;
				if(startTime < 0f) {
					endTime -= startTime;
					startTime = 0f;
				}
				if(endTime > Animation.TotalTime) {
					startTime -= endTime-Animation.TotalTime;
					endTime = Animation.TotalTime;
				}
				startTime = Mathf.Max(0f, startTime);
				endTime = Mathf.Min(Animation.TotalTime, endTime);
				int start = Animation.GetFrame(startTime).Index;
				int end = Animation.GetFrame(endTime).Index;
				int elements = end-start;

				//TODO REMOVE LATER
				if(NormalisedVelocities == null) {
					NormalisedVelocities = new float[Animation.TotalFrames];
				} else if(NormalisedVelocities.Length == 0) {
					NormalisedVelocities = new float[Animation.TotalFrames];
				}
				if(NormalisedCycle == null) {
					NormalisedCycle = new float[Animation.TotalFrames];
				} else if(NormalisedCycle.Length == 0) {
					NormalisedCycle = new float[Animation.TotalFrames];
				}
				//

				Vector3 prevPos = Vector3.zero;
				Vector3 newPos = Vector3.zero;
				Vector3 bottom = new Vector3(0f, rect.yMax, 0f);
				Vector3 top = new Vector3(0f, rect.yMax - rect.height, 0f);

				//Velocities
				for(int i=1; i<elements; i++) {
					prevPos.x = rect.xMin + (float)(i-1)/(elements-1) * rect.width;
					prevPos.y = rect.yMax - Animation.PhaseFunction.NormalisedVelocities[i+start-1] * rect.height;
					newPos.x = rect.xMin + (float)(i)/(elements-1) * rect.width;
					newPos.y = rect.yMax - Animation.PhaseFunction.NormalisedVelocities[i+start] * rect.height;
					UnityGL.DrawLine(prevPos, newPos, this == Animation.PhaseFunction ? Utility.Green : Utility.Red);
				}

				//Mirrored Velocities
				for(int i=1; i<elements; i++) {
					prevPos.x = rect.xMin + (float)(i-1)/(elements-1) * rect.width;
					prevPos.y = rect.yMax - Animation.MirroredPhaseFunction.NormalisedVelocities[i+start-1] * rect.height;
					newPos.x = rect.xMin + (float)(i)/(elements-1) * rect.width;
					newPos.y = rect.yMax - Animation.MirroredPhaseFunction.NormalisedVelocities[i+start] * rect.height;
					UnityGL.DrawLine(prevPos, newPos, this == Animation.PhaseFunction ? Utility.Red : Utility.Green);
				}

				//Heights
				/*
				for(int i=1; i<elements; i++) {
					prevPos.x = rect.xMin + (float)(i-1)/(elements-1) * rect.width;
					prevPos.y = rect.yMax - Heights[i+start-1] * rect.height;
					newPos.x = rect.xMin + (float)(i)/(elements-1) * rect.width;
					newPos.y = rect.yMax - Heights[i+start] * rect.height;
					UnityGL.DrawLine(prevPos, newPos, Utility.Red);
				}
				*/
				
				//Cycle
				if(ShowCycle) {
					for(int i=1; i<elements; i++) {
						prevPos.x = rect.xMin + (float)(i-1)/(elements-1) * rect.width;
						prevPos.y = rect.yMax - NormalisedCycle[i+start-1] * rect.height;
						newPos.x = rect.xMin + (float)(i)/(elements-1) * rect.width;
						newPos.y = rect.yMax - NormalisedCycle[i+start] * rect.height;
						UnityGL.DrawLine(prevPos, newPos, Utility.Yellow);
					}
				}

				//Phase
				BVHFrame A = Animation.GetFrame(start);
				if(A.Index == 1) {
					bottom.x = rect.xMin;
					top.x = rect.xMin;
					UnityGL.DrawLine(bottom, top, Utility.Magenta);
				}
				BVHFrame B = GetNextKey(A);
				while(A != B) {
					prevPos.x = rect.xMin + (float)(A.Index-start)/elements * rect.width;
					prevPos.y = rect.yMax - Mathf.Repeat(Phase[A.Index-1], 1f) * rect.height;
					newPos.x = rect.xMin + (float)(B.Index-start)/elements * rect.width;
					newPos.y = rect.yMax - Phase[B.Index-1] * rect.height;
					UnityGL.DrawLine(prevPos, newPos, Utility.White);
					bottom.x = rect.xMin + (float)(B.Index-start)/elements * rect.width;
					top.x = rect.xMin + (float)(B.Index-start)/elements * rect.width;
					UnityGL.DrawLine(bottom, top, Utility.Magenta);
					A = B;
					B = GetNextKey(A);
					if(B.Index > end) {
						break;
					}
				}

				//Seconds
				float timestamp = startTime;
				while(timestamp <= endTime) {
					float floor = Mathf.FloorToInt(timestamp);
					if(floor >= startTime && floor <= endTime) {
						top.x = rect.xMin + (float)(Animation.GetFrame(floor).Index-start)/elements * rect.width;
						UnityGL.DrawCircle(top, 5f, Utility.White);
					}
					timestamp += 1f;
				}
				//

				//Sequences
				for(int i=0; i<Animation.Sequences.Length; i++) {
					top.x = rect.xMin + (float)(Animation.Sequences[i].Start-start)/elements * rect.width;
					bottom.x = rect.xMin + (float)(Animation.Sequences[i].Start-start)/elements * rect.width;
					Vector3 a = top;
					Vector3 b = bottom;
					top.x = rect.xMin + (float)(Animation.Sequences[i].End-start)/elements * rect.width;
					bottom.x = rect.xMin + (float)(Animation.Sequences[i].End-start)/elements * rect.width;
					Vector3 c = top;
					Vector3 d = bottom;

					Color yellow = Utility.Yellow;
					yellow.a = 0.25f;
					UnityGL.DrawTriangle(a, b, c, yellow);
					UnityGL.DrawTriangle(d, b, c, yellow);
				}

				//Current Pivot
				top.x = rect.xMin + (float)(Animation.CurrentFrame.Index-start)/elements * rect.width;
				bottom.x = rect.xMin + (float)(Animation.CurrentFrame.Index-start)/elements * rect.width;
				UnityGL.DrawLine(top, bottom, Utility.Yellow);
				UnityGL.DrawCircle(top, 3f, Utility.Green);
				UnityGL.DrawCircle(bottom, 3f, Utility.Green);

				Handles.DrawLine(Vector3.zero, Vector3.zero); //Somehow needed to get it working...
				EditorGUILayout.EndVertical();

				if(Utility.GUIButton(">", Utility.DarkGrey, Utility.White, 25f, 50f)) {
					Animation.LoadFrame(GetNextKey(Animation.CurrentFrame));
				}
				EditorGUILayout.EndHorizontal();
			}

			UnityGL.Finish();
		}
	}

	[System.Serializable]
	public class BVHStyleFunction {
		public BVHAnimation Animation;
		
		public enum STYLE {Custom, Biped, Quadruped, Count}
		public STYLE Style = STYLE.Custom;

		public float Transition;
		public bool[] Keys;
		public BVHStyle[] Styles;

		public BVHStyleFunction(BVHAnimation animation) {
			Animation = animation;
			Reset();
		}

		public void Reset() {
			Transition = 0.25f;
			Keys = new bool[Animation.TotalFrames];
			Styles = new BVHStyle[0];
		}

		public void SetStyle(STYLE style) {
			if(Style != style) {
				Style = style;
				Reset();
				switch(Style) {
					case STYLE.Custom:
					break;

					case STYLE.Biped:
					AddStyle("Idle");
					AddStyle("Walk");
					AddStyle("Run");
					AddStyle("Crouch");
					AddStyle("Jump");
					AddStyle("Sit");
					break;

					case STYLE.Quadruped:
					AddStyle("Idle");
					AddStyle("Walk");
					AddStyle("Sprint");
					AddStyle("Jump");
					AddStyle("Sit");
					AddStyle("Lie");
					AddStyle("Stand");
					break;
				}
			}
		}

		public void AddStyle(string name = "Style") {
			System.Array.Resize(ref Styles, Styles.Length+1);
			Styles[Styles.Length-1] = new BVHStyle(name, Animation.TotalFrames);
		}

		public void RemoveStyle() {
			if(Styles.Length == 0) {
				return;
			}
			System.Array.Resize(ref Styles, Styles.Length-1);
		}

		public void SetFlag(BVHFrame frame, int dimension, bool value) {
			if(GetFlag(frame, dimension) == value) {
				return;
			}
			Styles[dimension].Flags[frame.Index-1] = value;
			Interpolate(frame, dimension);
		}

		public bool GetFlag(BVHFrame frame, int dimension) {
			return Styles[dimension].Flags[frame.Index-1];
		}

		public void SetKey(BVHFrame frame, bool value) {
			if(value) {
				if(IsKey(frame)) {
					return;
				}
				Keys[frame.Index-1] = true;
				Refresh();
			} else {
				if(!IsKey(frame)) {
					return;
				}
				Keys[frame.Index-1] = false;
				Refresh();
			}
		}

		public bool IsKey(BVHFrame frame) {
			return Keys[frame.Index-1];
		}

		public BVHFrame GetPreviousKey(BVHFrame frame) {
			if(frame != null) {
				for(int i=frame.Index-1; i>=1; i--) {
					if(Keys[i-1]) {
						return Animation.Frames[i-1];
					}
				}
			}
			return null;
		}

		public BVHFrame GetNextKey(BVHFrame frame) {
			if(frame != null) {
				for(int i=frame.Index+1; i<=Animation.TotalFrames; i++) {
					if(Keys[i-1]) {
						return Animation.Frames[i-1];
					}
				}
			}
			return null;
		}

		private void SetTransition(float value) {
			value = Mathf.Max(value, 0f);
			if(Transition == value) {
				return;
			}
			Transition = value;
			Refresh();
		}

		private void Refresh() {
			for(int i=0; i<Animation.TotalFrames; i++) {
				if(Keys[i]) {
					for(int j=0; j<Styles.Length; j++) {
						Interpolate(Animation.Frames[i], j);
					}
				}
			}
		}

		private void Interpolate(BVHFrame frame, int dimension) {
			BVHFrame prev = GetPreviousKey(frame);
			BVHFrame next = GetNextKey(frame);
			Styles[dimension].Values[frame.Index-1] = GetFlag(frame, dimension) ? 1f : 0f;
			if(IsKey(frame)) {
				MakeConstant(dimension, prev, frame);
				MakeConstant(dimension, frame, next);
			} else {
				MakeConstant(dimension, prev, next);
			}
			MakeTransition(dimension, prev);
			MakeTransition(dimension, frame);
			MakeTransition(dimension, next);
		}

		private void MakeConstant(int dimension, BVHFrame previous, BVHFrame next) {
			int start = previous == null ? 1 : previous.Index;
			int end = next == null ? Animation.TotalFrames : next.Index-1;
			for(int i=start; i<end; i++) {
				Styles[dimension].Flags[i] = Styles[dimension].Flags[start-1];
				Styles[dimension].Values[i] = Styles[dimension].Flags[start-1] ? 1f : 0f;
			}
		}

		private void MakeTransition(int dimension, BVHFrame frame) {
			if(frame == null) {
				return;
			}
			//Get window
			float window = GetWindow(frame);
			
			//Interpolate
			BVHFrame a = Animation.GetFrame(frame.Timestamp - 0.5f*window);
			BVHFrame b = Animation.GetFrame(frame.Timestamp + 0.5f*window);
			int dist = b.Index - a.Index;
			if(dist >= 2) {
				for(int i=a.Index+1; i<b.Index; i++) {
					float rateA = (float)((float)i-(float)a.Index)/(float)dist;
					float rateB = (float)((float)b.Index-(float)i)/(float)dist;
					rateA = rateA * rateA;
					rateB = rateB * rateB;
					float valueA = Styles[dimension].Flags[a.Index-1] ? 1f : 0f;
					float valueB = Styles[dimension].Flags[b.Index-1] ? 1f : 0f;
					Styles[dimension].Values[i-1] = rateB/(rateA+rateB)*valueA + rateA/(rateA+rateB)*valueB;
				}
			}
		}

		public float GetWindow(BVHFrame frame) {
			BVHFrame prev = GetPreviousKey(frame);
			BVHFrame next = GetNextKey(frame);
			float prevTS = prev == null ? 0f : prev.Timestamp;
			float nextTS = next == null ? Animation.TotalTime : next.Timestamp;
			float prevTime = Mathf.Abs(frame.Timestamp - prevTS);
			float nextTime = Mathf.Abs(frame.Timestamp - nextTS);
			float window = Mathf.Min(prevTime, nextTime, Transition);
			return window;
		}

		public void Inspector() {
			UnityGL.Start();

			Utility.SetGUIColor(Utility.LightGrey);
			using(new EditorGUILayout.VerticalScope ("Box")) {
				Utility.ResetGUIColor();

				Utility.SetGUIColor(Utility.Orange);
				using(new EditorGUILayout.VerticalScope ("Box")) {
					Utility.ResetGUIColor();
					EditorGUILayout.LabelField("Style Function");
				}

				SetTransition(EditorGUILayout.FloatField("Transition", Transition));

				string[] names = new string[(int)STYLE.Count];
				for(int i=0; i<names.Length; i++) {
					names[i] = ((STYLE)i).ToString();
				}
				SetStyle((STYLE)EditorGUILayout.Popup((int)Style, names));

				for(int i=0; i<Styles.Length; i++) {
					EditorGUILayout.BeginHorizontal();
					Styles[i].Name = EditorGUILayout.TextField(Styles[i].Name, GUILayout.Width(75f));
					if(IsKey(Animation.CurrentFrame)) {
						if(GetFlag(Animation.CurrentFrame, i)) {
							if(Utility.GUIButton("On", Utility.DarkGreen, Color.white)) {
								SetFlag(Animation.CurrentFrame, i, false);
							}
						} else {
							if(Utility.GUIButton("Off", Utility.DarkRed, Color.white)) {
								SetFlag(Animation.CurrentFrame, i, true);
							}
						}
					} else {
						EditorGUI.BeginDisabledGroup(true);
						if(GetFlag(Animation.CurrentFrame, i)) {
							if(Utility.GUIButton("On", Utility.DarkGreen, Color.white)) {
								SetFlag(Animation.CurrentFrame, i, false);
							}
						} else {
							if(Utility.GUIButton("Off", Utility.DarkRed, Color.white)) {
								SetFlag(Animation.CurrentFrame, i, true);
							}
						}
						EditorGUI.EndDisabledGroup();
					}
					EditorGUI.BeginDisabledGroup(true);
					EditorGUILayout.Slider(Styles[i].Values[Animation.CurrentFrame.Index-1], 0f, 1f);
					EditorGUI.EndDisabledGroup();
					EditorGUILayout.EndHorizontal();
				}
				
				if(Style == STYLE.Custom) {
				EditorGUILayout.BeginHorizontal();
					if(Utility.GUIButton("Add Style", Utility.DarkGrey, Utility.White)) {
						AddStyle();
					}
					if(Utility.GUIButton("Remove Style", Utility.DarkGrey, Utility.White)) {
						RemoveStyle();
					}
					EditorGUILayout.EndHorizontal();
				}

				if(IsKey(Animation.CurrentFrame)) {
					if(Utility.GUIButton("Unset Key", Utility.Grey, Utility.White)) {
						SetKey(Animation.CurrentFrame, false);
					}
				} else {
					if(Utility.GUIButton("Set Key", Utility.DarkGrey, Utility.White)) {
						SetKey(Animation.CurrentFrame, true);
					}
				}

				EditorGUILayout.BeginHorizontal();
				if(Utility.GUIButton("<", Utility.DarkGrey, Utility.White, 25f, 50f)) {
					Animation.LoadFrame(GetPreviousKey(Animation.CurrentFrame));
				}

				EditorGUILayout.BeginVertical(GUILayout.Height(50f));
				Rect ctrl = EditorGUILayout.GetControlRect();
				Rect rect = new Rect(ctrl.x, ctrl.y, ctrl.width, 50f);
				EditorGUI.DrawRect(rect, Utility.Black);

				float startTime = Animation.CurrentFrame.Timestamp-Animation.TimeWindow/2f;
				float endTime = Animation.CurrentFrame.Timestamp+Animation.TimeWindow/2f;
				if(startTime < 0f) {
					endTime -= startTime;
					startTime = 0f;
				}
				if(endTime > Animation.TotalTime) {
					startTime -= (endTime-Animation.TotalTime);
					endTime = Animation.TotalTime;
				}
				startTime = Mathf.Max(0f, startTime);
				endTime = Mathf.Min(Animation.TotalTime, endTime);
				int start = Animation.GetFrame(startTime).Index;
				int end = Animation.GetFrame(endTime).Index;
				int elements = end-start;

				Vector3 prevPos = Vector3.zero;
				Vector3 newPos = Vector3.zero;
				Vector3 bottom = new Vector3(0f, rect.yMax, 0f);
				Vector3 top = new Vector3(0f, rect.yMax - rect.height, 0f);
				
				Color[] colors = Utility.GetRainbowColors(Styles.Length);
				BVHFrame A = Animation.GetFrame(start);
				if(IsKey(A)) {
					bottom.x = rect.xMin;
					top.x = rect.xMin;
					UnityGL.DrawLine(bottom, top, Utility.Magenta);
				}
				
				BVHFrame B = GetNextKey(A);
				while(A != B && A != null && B != null) {
					float window = GetWindow(B);
					BVHFrame left = Animation.GetFrame(B.Timestamp - window/2f);
					BVHFrame right = Animation.GetFrame(B.Timestamp + window/2f);
					for(int f=left.Index; f<right.Index; f++) {
						prevPos.x = rect.xMin + (float)(f-start)/elements * rect.width;
						newPos.x = rect.xMin + (float)(f+1-start)/elements * rect.width;
						for(int i=0; i<Styles.Length; i++) {
							prevPos.y = rect.yMax - Styles[i].Values[f-1] * rect.height;
							newPos.y = rect.yMax - Styles[i].Values[f] * rect.height;
							UnityGL.DrawLine(prevPos, newPos, colors[i]);
						}
					}
					
					bottom.x = rect.xMin + (float)(B.Index-start)/elements * rect.width;
					top.x = rect.xMin + (float)(B.Index-start)/elements * rect.width;
					UnityGL.DrawLine(bottom, top, Utility.Magenta);
					
					A = B;
					B = GetNextKey(A);
					if(B == null) {
						break;
					}
					if(B.Index > end) {
						break;
					}
				}

				//Seconds
				float timestamp = startTime;
				while(timestamp <= endTime) {
					float floor = Mathf.FloorToInt(timestamp);
					if(floor >= startTime && floor <= endTime) {
						top.x = rect.xMin + (float)(Animation.GetFrame(floor).Index-start)/elements * rect.width;
						UnityGL.DrawCircle(top, 5f, Utility.White);
					}
					timestamp += 1f;
				}
				//

				//Sequences
				for(int i=0; i<Animation.Sequences.Length; i++) {
					top.x = rect.xMin + (float)(Animation.Sequences[i].Start-start)/elements * rect.width;
					bottom.x = rect.xMin + (float)(Animation.Sequences[i].Start-start)/elements * rect.width;
					Vector3 a = top;
					Vector3 b = bottom;
					top.x = rect.xMin + (float)(Animation.Sequences[i].End-start)/elements * rect.width;
					bottom.x = rect.xMin + (float)(Animation.Sequences[i].End-start)/elements * rect.width;
					Vector3 c = top;
					Vector3 d = bottom;

					Color yellow = Utility.Yellow;
					yellow.a = 0.25f;
					UnityGL.DrawTriangle(a, b, c, yellow);
					UnityGL.DrawTriangle(d, b, c, yellow);
				}

				//Current Pivot
				top.x = rect.xMin + (float)(Animation.CurrentFrame.Index-start)/elements * rect.width;
				bottom.x = rect.xMin + (float)(Animation.CurrentFrame.Index-start)/elements * rect.width;
				UnityGL.DrawLine(top, bottom, Utility.Yellow);
				UnityGL.DrawCircle(top, 3f, Utility.Green);
				UnityGL.DrawCircle(bottom, 3f, Utility.Green);

				Handles.DrawLine(Vector3.zero, Vector3.zero); //Somehow needed to get it working...
				EditorGUILayout.EndVertical();

				if(Utility.GUIButton(">", Utility.DarkGrey, Utility.White, 25f, 50f)) {
					Animation.LoadFrame(GetNextKey(Animation.CurrentFrame));
				}
				EditorGUILayout.EndHorizontal();

			}

			UnityGL.Finish();
		}
	}

	[System.Serializable]
	public class BVHStyle {
		public string Name;
		public bool[] Flags;
		public float[] Values;

		public BVHStyle(string name, int length) {
			Name = name;
			Flags = new bool[length];
			Values = new float[length];
		}
	}

	public class BVHEvolution {
		public static float AMPLITUDE = 10f;
		public static float FREQUENCY = 5f;
		public static float SHIFT = Mathf.PI;
		public static float OFFSET = 10f;
		public static float SLOPE = 5f;
		public static float WINDOW = 5f;
		
		public BVHAnimation Animation;
		public BVHPhaseFunction Function;

		public Population[] Populations;

		public float[] LowerBounds;
		public float[] UpperBounds;

		public float Amplitude = AMPLITUDE;
		public float Frequency = FREQUENCY;
		public float Shift = SHIFT;
		public float Offset = OFFSET;
		public float Slope = SLOPE;

		public float Behaviour = 1f;

		public float Window = 1f;
		public float Blending = 1f;

		public BVHEvolution(BVHAnimation animation, BVHPhaseFunction function) {
			Animation = animation;
			Function = function;

			LowerBounds = new float[5];
			UpperBounds = new float[5];

			SetAmplitude(Amplitude);
			SetFrequency(Frequency);
			SetShift(Shift);
			SetOffset(Offset);
			SetSlope(Slope);

			Initialise();
		}

		public void SetAmplitude(float value) {
			Amplitude = value;
			LowerBounds[0] = -value;
			UpperBounds[0] = value;
		}

		public void SetFrequency(float value) {
			Frequency = value;
			LowerBounds[1] = 0f;
			UpperBounds[1] = value;
		}

		public void SetShift(float value) {
			Shift = value;
			LowerBounds[2] = -value;
			UpperBounds[2] = value;
		}

		public void SetOffset(float value) {
			Offset = value;
			LowerBounds[3] = -value;
			UpperBounds[3] = value;
		}

		public void SetSlope(float value) {
			Slope = value;
			LowerBounds[4] = -value;
			UpperBounds[4] = value;
		}

		public void SetWindow(float value) {
			if(Window != value) {
				Window = value;
				Initialise();
			}
		}

		public void Initialise() {
			Interval[] intervals = new Interval[Mathf.FloorToInt(Animation.TotalTime / Window) + 1];
			for(int i=0; i<intervals.Length; i++) {
				int start = Animation.GetFrame(i*Window).Index-1;
				int end = Animation.GetFrame(Mathf.Min(Animation.TotalTime, (i+1)*Window)).Index-2;
				if(end == Animation.TotalFrames-2) {
					end += 1;
				}
				intervals[i] = new Interval(start, end);
			}
			Populations = new Population[intervals.Length];
			for(int i=0; i<Populations.Length; i++) {
				Populations[i] = new Population(this, 50, 5, intervals[i]);
			}
			Assign();
		}

		public void Optimise() {
			for(int i=0; i<Populations.Length; i++) {
				Populations[i].Active = IsActive(i);
			}
			for(int i=0; i<Populations.Length; i++) {
				Populations[i].Evolve(GetPreviousPopulation(i), GetNextPopulation(i), GetPreviousPivotPopulation(i), GetNextPivotPopulation(i));
			}
			Assign();
		}

		
		public void Assign() {
			for(int i=0; i<Animation.TotalFrames; i++) {
				Function.Keys[i] = false;
				Function.Phase[i] = 0f;
				Function.Cycle[i] = 0f;
				Function.NormalisedCycle[i] = 0f;
			}

			//Compute cycle
			float min = float.MaxValue;
			float max = float.MinValue;
			for(int i=0; i<Populations.Length; i++) {
				for(int j=Populations[i].Interval.Start; j<=Populations[i].Interval.End; j++) {
					Function.Cycle[j] = Interpolate(i, j);
					min = Mathf.Min(min, Function.Cycle[j]);
					max = Mathf.Max(max, Function.Cycle[j]);
				}
			}
			for(int i=0; i<Populations.Length; i++) {
				for(int j=Populations[i].Interval.Start; j<=Populations[i].Interval.End; j++) {
					Function.NormalisedCycle[j] = Utility.Normalise(Function.Cycle[j], min, max, 0f, 1f);
				}
			}

			//Fill with frequency negative turning points
			for(int i=0; i<Populations.Length; i++) {
				for(int j=Populations[i].Interval.Start; j<=Populations[i].Interval.End; j++) {
					if(InterpolateD2(i, j) <= 0f && InterpolateD2(i, j+1) >= 0f) {
						Function.Keys[j] = true;
					}
				}
			}

			//Compute phase
			for(int i=0; i<Function.Keys.Length; i++) {
				if(Function.Keys[i]) {
					Function.SetPhase(Animation.Frames[i], i == 0 ? 0f : 1f);
				}
			}
		}

		public Population GetPreviousPopulation(int current) {
			return Populations[Mathf.Max(0, current-1)];
		}

		public Population GetPreviousPivotPopulation(int current) {
			for(int i=current-1; i>=0; i--) {
				if(Populations[i].Active) {
					return Populations[i];
				}
			}
			return Populations[0];
		}

		public Population GetNextPopulation(int current) {
			return Populations[Mathf.Min(Populations.Length-1, current+1)];
		}

		public Population GetNextPivotPopulation(int current) {
			for(int i=current+1; i<Populations.Length; i++) {
				if(Populations[i].Active) {
					return Populations[i];
				}
			}
			return Populations[Populations.Length-1];
		}

		public bool IsActive(int interval) {
			float velocity = 0f;
			for(int i=Populations[interval].Interval.Start; i<=Populations[interval].Interval.End; i++) {
				velocity += Function.Velocities[i];
				velocity += Function == Animation.PhaseFunction ? Animation.MirroredPhaseFunction.Velocities[i] : Animation.PhaseFunction.Velocities[i];
			}
			return velocity / Populations[interval].Interval.Length > 0f;
		}

		public float Interpolate(int interval, int frame) {
			interval = Mathf.Clamp(interval, 0, Populations.Length-1);
			Population current = Populations[interval];
			float value = current.Phenotype(current.GetWinner().Genes, frame);
			float pivot = (float)(frame-current.Interval.Start) / (float)(current.Interval.Length-1) - 0.5f;
			float threshold = 0.5f * (1f - Blending);
			if(pivot < -threshold) {
				Population previous = GetPreviousPopulation(interval);
				float blend = 0.5f * (pivot + threshold) / (-0.5f + threshold);
				float prevValue = previous.Phenotype(previous.GetWinner().Genes, frame);
				value = (1f-blend) * value + blend * prevValue;
			}
			if(pivot > threshold) {
				Population next = GetNextPopulation(interval);
				float blend = 0.5f * (pivot - threshold) / (0.5f - threshold);
				float nextValue = next.Phenotype(next.GetWinner().Genes, frame);
				value = (1f-blend) * value + blend * nextValue;
			}
			return value;
		}

		public float InterpolateD1(int interval, int frame) {
			interval = Mathf.Clamp(interval, 0, Populations.Length-1);
			Population current = Populations[interval];
			float value = current.Phenotype1(current.GetWinner().Genes, frame);
			float pivot = (float)(frame-current.Interval.Start) / (float)(current.Interval.Length-1) - 0.5f;
			float threshold = 0.5f * (1f - Blending);
			if(pivot < -threshold) {
				Population previous = GetPreviousPopulation(interval);
				float blend = 0.5f * (pivot + threshold) / (-0.5f + threshold);
				float prevValue = previous.Phenotype1(previous.GetWinner().Genes, frame);
				value = (1f-blend) * value + blend * prevValue;
			}
			if(pivot > threshold) {
				Population next = GetNextPopulation(interval);
				float blend = 0.5f * (pivot - threshold) / (0.5f - threshold);
				float nextValue = next.Phenotype1(next.GetWinner().Genes, frame);
				value = (1f-blend) * value + blend * nextValue;
			}
			return value;
		}

		public float InterpolateD2(int interval, int frame) {
			interval = Mathf.Clamp(interval, 0, Populations.Length-1);
			Population current = Populations[interval];
			float value = current.Phenotype2(current.GetWinner().Genes, frame);
			float pivot = (float)(frame-current.Interval.Start) / (float)(current.Interval.Length-1) - 0.5f;
			float threshold = 0.5f * (1f - Blending);
			if(pivot < -threshold) {
				Population previous = GetPreviousPopulation(interval);
				float blend = 0.5f * (pivot + threshold) / (-0.5f + threshold);
				float prevValue = previous.Phenotype2(previous.GetWinner().Genes, frame);
				value = (1f-blend) * value + blend * prevValue;
			}
			if(pivot > threshold) {
				Population next = GetNextPopulation(interval);
				float blend = 0.5f * (pivot - threshold) / (0.5f - threshold);
				float nextValue = next.Phenotype2(next.GetWinner().Genes, frame);
				value = (1f-blend) * value + blend * nextValue;
			}
			return value;
		}

		public float InterpolateD3(int interval, int frame) {
			interval = Mathf.Clamp(interval, 0, Populations.Length-1);
			Population current = Populations[interval];
			float value = current.Phenotype3(current.GetWinner().Genes, frame);
			float pivot = (float)(frame-current.Interval.Start) / (float)(current.Interval.Length-1) - 0.5f;
			float threshold = 0.5f * (1f - Blending);
			if(pivot < -threshold) {
				Population previous = GetPreviousPopulation(interval);
				float blend = 0.5f * (pivot + threshold) / (-0.5f + threshold);
				float prevValue = previous.Phenotype3(previous.GetWinner().Genes, frame);
				value = (1f-blend) * value + blend * prevValue;
			}
			if(pivot > threshold) {
				Population next = GetNextPopulation(interval);
				float blend = 0.5f * (pivot - threshold) / (0.5f - threshold);
				float nextValue = next.Phenotype3(next.GetWinner().Genes, frame);
				value = (1f-blend) * value + blend * nextValue;
			}
			return value;
		}

		public float GetFitness() {
			float fitness = 0f;
			for(int i=0; i<Populations.Length; i++) {
				fitness += Populations[i].GetFitness();
			}
			return fitness / Populations.Length;
		}

		public float[] GetPeakConfiguration() {
			float[] configuration = new float[5];
			for(int i=0; i<5; i++) {
				configuration[i] = float.MinValue;
			}
			for(int i=0; i<Populations.Length; i++) {
				for(int j=0; j<5; j++) {
					configuration[j] = Mathf.Max(configuration[j], Mathf.Abs(Populations[i].GetWinner().Genes[j]));
				}
			}
			return configuration;
		}

		public class Population {
			public BVHEvolution Evolution;
			public int Size;
			public int Dimensionality;
			public Interval Interval;

			public bool Active;

			public Individual[] Individuals;
			public Individual[] Offspring;
			public float[] RankProbabilities;
			public float RankProbabilitySum;

			public Population(BVHEvolution evolution, int size, int dimensionality, Interval interval) {
				Evolution = evolution;
				Size = size;
				Dimensionality = dimensionality;
				Interval = interval;

				//Create individuals
				Individuals = new Individual[Size];
				Offspring = new Individual[Size];
				for(int i=0; i<Size; i++) {
					Individuals[i] = new Individual(Dimensionality);
					Offspring[i] = new Individual(Dimensionality);
				}

				//Compute rank probabilities
				RankProbabilities = new float[Size];
				float rankSum = (float)(Size*(Size+1)) / 2f;
				for(int i=0; i<Size; i++) {
					RankProbabilities[i] = (float)(Size-i)/(float)rankSum;
				}
				for(int i=0; i<Size; i++) {
					RankProbabilitySum += RankProbabilities[i];
				}

				//Initialise randomly
				for(int i=0; i<Size; i++) {
					Reroll(Individuals[i]);
				}

				//Evaluate fitness
				for(int i=0; i<Size; i++) {
					Individuals[i].Fitness = ComputeFitness(Individuals[i].Genes);
				}

				//Sort
				SortByFitness(Individuals);

				//Evaluate extinctions
				AssignExtinctions(Individuals);
			}

			public void Evolve(Population previous, Population next, Population previousPivot, Population nextPivot) {
				if(Active) {
					//Copy elite
					Copy(Individuals[0], Offspring[0]);

					//Memetic exploitation
					Exploit(Offspring[0]);

					//Remaining individuals
					for(int o=1; o<Size; o++) {
						Individual offspring = Offspring[o];
						if(Random.value <= Evolution.Behaviour) {
							Individual parentA = Select(Individuals);
							Individual parentB = Select(Individuals);
							while(parentB == parentA) {
								parentB = Select(Individuals);
							}
							Individual prototype = Select(Individuals);
							while(prototype == parentA || prototype == parentB) {
								prototype = Select(Individuals);
							}

							float mutationRate = GetMutationProbability(parentA, parentB);
							float mutationStrength = GetMutationStrength(parentA, parentB);

							for(int i=0; i<Dimensionality; i++) {
								float weight;

								//Recombination
								weight = Random.value;
								float momentum = Random.value * parentA.Momentum[i] + Random.value * parentB.Momentum[i];
								if(Random.value < 0.5f) {
									offspring.Genes[i] = parentA.Genes[i] + momentum;
								} else {
									offspring.Genes[i] = parentB.Genes[i] + momentum;
								}

								//Store
								float gene = offspring.Genes[i];

								//Mutation
								if(Random.value <= mutationRate) {
									float span = Evolution.UpperBounds[i] - Evolution.LowerBounds[i];
									offspring.Genes[i] += Random.Range(-mutationStrength*span, mutationStrength*span);
								}
								
								//Adoption
								weight = Random.value;
								offspring.Genes[i] += 
									weight * Random.value * (0.5f * (parentA.Genes[i] + parentB.Genes[i]) - offspring.Genes[i])
									+ (1f-weight) * Random.value * (prototype.Genes[i] - offspring.Genes[i]);

								//Constrain
								offspring.Genes[i] = Mathf.Clamp(offspring.Genes[i], Evolution.LowerBounds[i], Evolution.UpperBounds[i]);

								//Momentum
								offspring.Momentum[i] = Random.value * momentum + (offspring.Genes[i] - gene);
							}
						} else {
							Reroll(offspring);
						}
					}

					//Evaluate fitness
					for(int i=0; i<Size; i++) {
						Offspring[i].Fitness = ComputeFitness(Offspring[i].Genes);
					}

					//Sort
					SortByFitness(Offspring);

					//Evaluate extinctions
					AssignExtinctions(Offspring);

					//Form new population
					for(int i=0; i<Size; i++) {
						Copy(Offspring[i], Individuals[i]);
					}
				} else {
					//Postprocess
					for(int i=0; i<Size; i++) {
						Individuals[i].Genes[0] = 1f;
						Individuals[i].Genes[1] = 1f;
						Individuals[i].Genes[2] = 0.5f * (previousPivot.GetWinner().Genes[2] + nextPivot.GetWinner().Genes[2]);
						Individuals[i].Genes[3] = 0.5f * (previousPivot.GetWinner().Genes[3] + nextPivot.GetWinner().Genes[3]);
						Individuals[i].Genes[4] = 0f;
						for(int j=0; j<5; j++) {
							Individuals[i].Momentum[j] = 0f;
						}
						Individuals[i].Fitness = 0f;
						Individuals[i].Extinction = 0f;
					}
				}
			}

			//Returns the mutation probability from two parents
			private float GetMutationProbability(Individual parentA, Individual parentB) {
				float extinction = 0.5f * (parentA.Extinction + parentB.Extinction);
				float inverse = 1f/(float)Dimensionality;
				return extinction * (1f-inverse) + inverse;
			}

			//Returns the mutation strength from two parents
			private float GetMutationStrength(Individual parentA, Individual parentB) {
				return 0.5f * (parentA.Extinction + parentB.Extinction);
			}

			public Individual GetWinner() {
				return Individuals[0];
			}

			public float GetFitness() {
				return GetWinner().Fitness;
			}

			private void Copy(Individual from, Individual to) {
				for(int i=0; i<Dimensionality; i++) {
					to.Genes[i] = Mathf.Clamp(from.Genes[i], Evolution.LowerBounds[i], Evolution.UpperBounds[i]);
					to.Momentum[i] = from.Momentum[i];
				}
				to.Extinction = from.Extinction;
				to.Fitness = from.Fitness;
			}

			private void Reroll(Individual individual) {
				for(int i=0; i<Dimensionality; i++) {
					individual.Genes[i] = Random.Range(Evolution.LowerBounds[i], Evolution.UpperBounds[i]);
				}
			}

			private void Exploit(Individual individual) {
				individual.Fitness = ComputeFitness(individual.Genes);
				for(int i=0; i<Dimensionality; i++) {
					float gene = individual.Genes[i];

					float span = Evolution.UpperBounds[i] - Evolution.LowerBounds[i];

					float incGene = Mathf.Clamp(gene + Random.value*individual.Fitness*span, Evolution.LowerBounds[i], Evolution.UpperBounds[i]);
					individual.Genes[i] = incGene;
					float incFitness = ComputeFitness(individual.Genes);

					float decGene = Mathf.Clamp(gene - Random.value*individual.Fitness*span, Evolution.LowerBounds[i], Evolution.UpperBounds[i]);
					individual.Genes[i] = decGene;
					float decFitness = ComputeFitness(individual.Genes);

					individual.Genes[i] = gene;

					if(incFitness < individual.Fitness) {
						individual.Genes[i] = incGene;
						individual.Momentum[i] = incGene - gene;
						individual.Fitness = incFitness;
					}

					if(decFitness < individual.Fitness) {
						individual.Genes[i] = decGene;
						individual.Momentum[i] = decGene - gene;
						individual.Fitness = decFitness;
					}
				}
			}

			//Rank-based selection of an individual
			private Individual Select(Individual[] pool) {
				double rVal = Random.value * RankProbabilitySum;
				for(int i=0; i<Size; i++) {
					rVal -= RankProbabilities[i];
					if(rVal <= 0.0) {
						return pool[i];
					}
				}
				return pool[Size-1];
			}

			//Sorts all individuals starting with best (lowest) fitness
			private void SortByFitness(Individual[] individuals) {
				System.Array.Sort(individuals,
					delegate(Individual a, Individual b) {
						return a.Fitness.CompareTo(b.Fitness);
					}
				);
			}

			//Multi-Objective RMSE
			private float ComputeFitness(float[] genes) {
				float fitness = 0f;
				for(int i=Interval.Start; i<=Interval.End; i++) {
					float y1 = Evolution.Function.Velocities[i];
					float y2 = Evolution.Function == Evolution.Animation.PhaseFunction ? Evolution.Animation.MirroredPhaseFunction.Velocities[i] : Evolution.Animation.PhaseFunction.Velocities[i];
					float x = Phenotype(genes, i);
					float error = (y1-x)*(y1-x) + (-y2-x)*(-y2-x);
					float sqrError = error*error;
					fitness += sqrError;
				}
				fitness /= Interval.Length;
				fitness = Mathf.Sqrt(fitness);
				return fitness;
			}
			
			public float Phenotype(float[] genes, int frame) {
				return Utility.LinSin(
					genes[0], 
					genes[1], 
					genes[2], 
					genes[3] - (float)(frame-Interval.Start)*genes[4]*Evolution.Animation.FrameTime, 
					genes[4], 
					frame*Evolution.Animation.FrameTime
					);
			}

			public float Phenotype1(float[] genes, int frame) {
				return Utility.LinSin1(
					genes[0], 
					genes[1], 
					genes[2], 
					genes[3] - (float)(frame-Interval.Start)*genes[4]*Evolution.Animation.FrameTime, 
					genes[4], 
					frame*Evolution.Animation.FrameTime
					);
			}

			public float Phenotype2(float[] genes, int frame) {
				return Utility.LinSin2(
					genes[0], 
					genes[1], 
					genes[2], 
					genes[3] - (float)(frame-Interval.Start)*genes[4]*Evolution.Animation.FrameTime, 
					genes[4], 
					frame*Evolution.Animation.FrameTime
					);
			}

			public float Phenotype3(float[] genes, int frame) {
				return Utility.LinSin3(
					genes[0], 
					genes[1], 
					genes[2], 
					genes[3] - (float)(frame-Interval.Start)*genes[4]*Evolution.Animation.FrameTime, 
					genes[4], 
					frame*Evolution.Animation.FrameTime
					);
			}

			//Compute extinction values
			private void AssignExtinctions(Individual[] individuals) {
				float min = individuals[0].Fitness;
				float max = individuals[Size-1].Fitness;
				for(int i=0; i<Size; i++) {
					float grading = (float)i/((float)Size-1);
					individuals[i].Extinction = (individuals[i].Fitness + min*(grading-1f)) / max;
				}
			}
		}

		public class Individual {
			public float[] Genes;
			public float[] Momentum;
			public float Extinction;
			public float Fitness;
			public Individual(int dimensionality) {
				Genes = new float[dimensionality];
				Momentum = new float[dimensionality];
			}
		}

		public class Interval {
			public int Start;
			public int End;
			public int Length;
			public Interval(int start, int end) {
				Start = start;
				End = end;
				Length = end-start+1;
			}
		}

	}

}