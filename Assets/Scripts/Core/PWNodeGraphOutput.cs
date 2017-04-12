﻿using UnityEditor;
using UnityEngine;

namespace PW
{
	[System.SerializableAttribute]
	public class PWNodeGraphOutput : PWNode {

		//allow everything as output type
		[PWMultiple(0, typeof(object))]
		[PWInput("in")]
		[PWOffset(0, 20)]
		public PWValues		inputValues = new PWValues();

		public override void OnNodeCreate()
		{

		}

		public override void OnNodeGUI()
		{
			var names = inputValues.GetNames< object >();
			var values = inputValues.GetValues< object >();

			EditorGUILayout.LabelField("names: [" + names.Count + "]");
			for (int i = 0; i < names.Count; i++)
			{
				if (names[i] != null && values[i] != null)
					EditorGUILayout.LabelField(names[i] + " <" /*+ values[i].GetType() + ": "*/ + values[i] + ">");
				else
					EditorGUILayout.LabelField("null");
			}

			//TODO: dynamically remove unlinked nodes.
		}

		public override void OnNodeProcess()
		{
		}
	}
}
