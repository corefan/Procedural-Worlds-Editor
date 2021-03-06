﻿using UnityEngine;
using ProceduralWorlds.Core;

namespace ProceduralWorlds.Node
{
	public class NodeGameObject : BaseNode
	{

		[Output("object")]
		public GameObject		outputGameObject;

		public bool					showPreview;

		public override void OnNodeCreation()
		{
			name = "GameObject";
			renamable = true;
		}

		//no process needed
	}
}
