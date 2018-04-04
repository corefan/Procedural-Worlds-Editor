﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using ProceduralWorlds;
using ProceduralWorlds.Core;
using ProceduralWorlds.IsoSurfaces;

namespace ProceduralWorlds.Editor
{
	[CustomEditor(typeof(TerrainGenericBase))]
	public abstract class TerrainBaseInspector : UnityEditor.Editor
	{
		TerrainGenericBase baseTerrain;

		public void OnEnable()
		{
			baseTerrain = target as TerrainGenericBase;
			OnEditorEnable();
		}

		public override void OnInspectorGUI()
		{
			baseTerrain.debug = EditorGUILayout.Toggle("Debug", baseTerrain.debug);
			baseTerrain.renderDistance = EditorGUILayout.IntSlider("Render distance", baseTerrain.renderDistance, 0, 24);
			baseTerrain.loadPatternMode = (ChunkLoadPatternMode)EditorGUILayout.EnumPopup("Load pattern mode", baseTerrain.loadPatternMode);
			baseTerrain.terrainStorage = EditorGUILayout.ObjectField("Chunk storage", baseTerrain.terrainStorage, typeof(TerrainStorage), false) as TerrainStorage;
			baseTerrain.terrainScale = EditorGUILayout.Slider("Terrain scale", baseTerrain.terrainScale, 0.01f, 10f);
			baseTerrain.graphAsset = EditorGUILayout.ObjectField("World Graph", baseTerrain.graphAsset, typeof(WorldGraph), false) as WorldGraph;

			OnEditorGUI();
		}

		public void OnSceneGUI()
		{
			OnEditorSceneGUI();
		}

		public abstract void OnEditorGUI();
		public abstract void OnEditorEnable();
		public virtual void OnEditorSceneGUI() {}
	}
}