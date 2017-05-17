﻿// #define DEBUG_WINDOW

using UnityEditor;
using UnityEngine;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;

namespace PW
{
	[System.SerializableAttribute]
	public class PWNode : ScriptableObject
	{
		public static int	windowRenderOrder = 0;

		public string	nodeTypeName;
		public Rect		windowRect;
		public int		windowId;
		public bool		renamable;
		public int		computeOrder;
		public int		viewHeight;
		public bool		specialButtonClick = false;
		public bool		isDragged = false;
		public Vector3	chunkPosition = Vector3.zero;
		public int		chunkSize = 16;
		public int		seed;
		public float	step;
		public float	processTime = 0f;
		public string	externalName;
		[SerializeField]
		public PWGUIManager	PWGUI;

		//useful state bools
		public bool		seedHasChanged = false;
		public bool		positionHasChanged = false;
		public bool		chunkSizeHasChanged = false;
		public bool		stepHasChanged = false;
		public bool		inputHasChanged = false;
		public bool		outputHasChanged = false;
		public bool		justReloaded = false;
		public bool		notifyDataChanged = false;
		public bool		reloadRequested = false;
		public bool		needUpdate { get { return seedHasChanged || positionHasChanged || chunkSizeHasChanged || stepHasChanged || inputHasChanged || justReloaded || reloadRequested;}}
		public bool		isDependent {get; private set; }

	#region Internal Node datas and style
		static GUIStyle		boxAnchorStyle = null;
		static GUIStyle 	renameNodeTextFieldStyle = null;
		static GUIStyle		inputAnchorLabelStyle = null;
		static GUIStyle		outputAnchorLabelStyle = null;
		
		static Texture2D	errorIcon = null;
		static Texture2D	editIcon = null;
		static Texture2D	anchorTexture = null;
		static Texture2D	anchorDisabledTexture = null;

		static Color		anchorAttachAddColor = new Color(.1f, .1f, .9f);
		static Color		anchorAttachNewColor = new Color(.1f, .9f, .1f);
		static Color		anchorAttachReplaceColor = new Color(.9f, .1f, .1f);

		[SerializeField]
		Vector2				graphDecal;
		[SerializeField]
		int					maxAnchorRenderHeight;
		[SerializeField]
		string				firstInitialization;

		bool				windowShouldClose = false;

		Vector3				oldChunkPosition;
		int					oldSeed;
		int					oldChunkSize;
		float				oldStep;
		Pair< string, int >	lastAttachedLink = null;

		PWAnchorInfo		anchorUnderMouse;

		//node links and deps
		[SerializeField]
		List< PWLink >					links = new List< PWLink >();
		[SerializeField]
		List< PWNodeDependency >		depencendies = new List< PWNodeDependency >(); //List< windowId, anchorId >

		//backed datas about properties and nodes
		[System.SerializableAttribute]
		public class PropertyDataDictionary : SerializableDictionary< string, PWAnchorData > {}
		[SerializeField]
		protected PropertyDataDictionary			propertyDatas = new PropertyDataDictionary();

		[NonSerializedAttribute]
		protected Dictionary< string, FieldInfo >	bakedNodeFields = new Dictionary< string, FieldInfo >();

		[System.NonSerializedAttribute]
		public bool		unserializeInitialized = false;

		public bool		windowNameEdit = false;
		public bool		selected = false;

		public GUIStyle	windowStyle;
		public GUIStyle windowSelectedStyle;

	#endregion

	#region OnEnable, data initialization and baking
		public void OnEnable()
		{
			Func< string, Texture2D > CreateTexture2DFromFile = (string ressourcePath) => {
				return Resources.Load< Texture2D >(ressourcePath);
			};
				
			errorIcon = CreateTexture2DFromFile("ic_error");
			editIcon = CreateTexture2DFromFile("ic_edit");

			LoadFieldAttributes();

			BakeNodeFields();
			
			//this will be true only if the object instance does not came from a serialized object.
			if (firstInitialization == null)
			{
				computeOrder = 0;
				windowRect = new Rect(400, 400, 200, 50);
				viewHeight = 0;
				renamable = false;
				maxAnchorRenderHeight = 0;
				PWGUI = new PWGUIManager();

				OnNodeCreateOnce();

				firstInitialization = "initialized";
			}

			justReloaded = true;
		}
		
		void BakeNodeFields()
		{
			System.Reflection.FieldInfo[] fInfos = GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);

			bakedNodeFields.Clear();
			foreach (var fInfo in fInfos)
				bakedNodeFields[fInfo.Name] = fInfo;
		}

		public static Color GetAnchorColorByType(Type t)
		{
			if (t == typeof(float) || t == typeof(Vector2) || t == typeof(Vector3) || t == typeof(Vector4))
				return PWColorPalette.GetColor("yellowAnchor");
			else if (t.IsSubclassOf(typeof(ChunkData)) || t == typeof(ChunkData))
				return PWColorPalette.GetColor("blueAnchor");
			else if (t.IsSubclassOf(typeof(BiomeData)) || t == typeof(BiomeData))
				return PWColorPalette.GetColor("purpleAnchor");
			else if (t.IsSubclassOf(typeof(Sampler)) || t == typeof(Sampler))
				return PWColorPalette.GetColor("greenAnchor");
			else if (t == typeof(PWValues) || t == typeof(PWValue))
				return PWColorPalette.GetColor("whiteAnchor");
			else
				return PWColorPalette.GetColor("defaultAnchor");
		}

		void LoadFieldAttributes()
		{
			//get input variables
			System.Reflection.FieldInfo[] fInfos = GetType().GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

			List< string > actualFields = new List< string >();
			foreach (var field in fInfos)
			{
				actualFields.Add(field.Name);
				if (!propertyDatas.ContainsKey(field.Name))
					propertyDatas[field.Name] = new PWAnchorData(field.Name, field.Name.GetHashCode());
				
				PWAnchorData	data = propertyDatas[field.Name];
				Color			backgroundColor = GetAnchorColorByType(field.FieldType);
				PWAnchorType	anchorType = PWAnchorType.None;
				string			name = field.Name;
				Vector2			offset = Vector2.zero;

				data.anchorInstance = field.GetValue(this);
				System.Object[] attrs = field.GetCustomAttributes(true);
				foreach (var attr in attrs)
				{
					PWInput			inputAttr = attr as PWInput;
					PWOutput		outputAttr = attr as PWOutput;
					PWColor			colorAttr = attr as PWColor;
					PWOffset		offsetAttr = attr as PWOffset;
					PWMultiple		multipleAttr = attr as PWMultiple;
					PWGeneric		genericAttr = attr as PWGeneric;
					PWMirror		mirrorAttr = attr as PWMirror;
					PWNotRequired	notRequiredAttr = attr as PWNotRequired;

					if (inputAttr != null)
					{
						anchorType = PWAnchorType.Input;
						if (inputAttr.name != null)
						{
							name = inputAttr.name;
							data.anchorName = inputAttr.name;
						}
					}
					if (outputAttr != null)
					{
						anchorType = PWAnchorType.Output;
						if (outputAttr.name != null)
						{
							name = outputAttr.name;
							data.anchorName = outputAttr.name;
						}
					}
					if (colorAttr != null)
						backgroundColor = colorAttr.color;
					if (offsetAttr != null)
						offset = offsetAttr.offset;
					if (multipleAttr != null)
					{
						//check if field is PWValues type otherwise do not implement multi-anchor
						data.generic = true;
						data.multiple = true;
						data.allowedTypes = multipleAttr.allowedTypes;
						data.minMultipleValues = multipleAttr.minValues;
						data.maxMultipleValues = multipleAttr.maxValues;
					}
					if (genericAttr != null)
					{
						data.allowedTypes = genericAttr.allowedTypes;
						data.generic = true;
					}
					if (mirrorAttr != null)
						data.mirroredField = mirrorAttr.fieldName;
					if (notRequiredAttr != null)
						data.required = false;
				}
				if (anchorType == PWAnchorType.None) //field does not have a PW attribute
					propertyDatas.Remove(field.Name);
				else
				{
					if (data.required && anchorType == PWAnchorType.Output)
						data.required = false;
					data.classAQName = GetType().AssemblyQualifiedName;
					data.fieldName = field.Name;
					data.anchorType = anchorType;
					data.type = (SerializableType)field.FieldType;
					data.first.color = (SerializableColor)backgroundColor;
					data.first.linkType = GetLinkTypeFromType(field.FieldType);
					data.first.name = name;
					data.offset = offset;
					data.windowId = windowId;

					//add missing values to instance of list:

					if (data.multiple && data.anchorInstance != null)
					{
						//add minimum number of anchors to render:
						if (data.multipleValueCount < data.minMultipleValues)
							for (int i = data.multipleValueCount; i < data.minMultipleValues; i++)
								data.AddNewAnchor(backgroundColor, field.Name.GetHashCode() + i + 1);

						var PWValuesInstance = data.anchorInstance as PWValues;

						if (PWValuesInstance != null)
							while (PWValuesInstance.Count < data.multipleValueCount)
								PWValuesInstance.Add(null);
					}
				}
			}

			//Check mirrored fields compatibility and if node do not need input to work:
			isDependent = false;
			foreach (var kp in propertyDatas)
			{
				if (kp.Value.mirroredField != null)
				{
					if (propertyDatas.ContainsKey(kp.Value.mirroredField))
					{
						var type = propertyDatas[kp.Value.mirroredField].type;
						if (type != kp.Value.type)
						{
							Debug.LogWarning("incompatible mirrored type in " + GetType());
							kp.Value.mirroredField = null;
							continue ;
						}
					}
					else
						kp.Value.mirroredField = null;
				}
				if (kp.Value.anchorType == PWAnchorType.Input && kp.Value.required)
					isDependent = true;
			}

			//remove inhexistants dictionary entries:
			var toRemoveKeys = new List< string >();
			foreach (var kp in propertyDatas)
				if (!actualFields.Contains(kp.Key))
					toRemoveKeys.Add(kp.Key);
			foreach (var toRemoveKey in toRemoveKeys)
				propertyDatas.Remove(toRemoveKey);
		}

	#endregion

	#region inheritence virtual API

		public virtual void OnNodeAwake()
		{
		}

		public virtual void OnNodeCreate()
		{
		}

		public virtual void OnNodeCreateOnce()
		{
		}

		public virtual void OnNodeProcess()
		{
		}

		public virtual void	OnNodeAnchorLink(string propName, int index)
		{
		}

		public virtual void OnNodeAnchorUnlink(string propName, int index)
		{
		}

	#endregion

	#region Node rendering and processing

		#if UNITY_EDITOR
		public void OnWindowGUI(int id)
		{
			var e = Event.current;

			if (boxAnchorStyle == null)
			{
				boxAnchorStyle = new GUIStyle(GUI.skin.box);
				boxAnchorStyle.padding = new RectOffset(0, 0, 1, 1);
				anchorTexture = GUI.skin.box.normal.background;
				anchorDisabledTexture = GUI.skin.box.active.background;
				renameNodeTextFieldStyle = new GUIStyle(GUI.skin.FindStyle("RenameNodetextField"));
			}
			//TODO: put this in the if above
			inputAnchorLabelStyle = GUI.skin.FindStyle("InputAnchorLabel");
			outputAnchorLabelStyle = GUI.skin.FindStyle("OutputAnchorLabel");

			//update the PWGUI window rect with this window rect:
			PWGUI.currentWindowRect = windowRect;
			PWGUI.StartFrame();

			// set the header of the window as draggable:
			int width = (int) windowRect.width;
			Rect dragRect = new Rect(0, 0, width, 20);
			if (e.type == EventType.MouseDown && dragRect.Contains(e.mousePosition))
				isDragged = true;
			if (e.type == EventType.MouseUp)
				isDragged = false;
			if (id != -1 && e.button == 0 && !windowNameEdit)
				GUI.DragWindow(dragRect);

			int	debugViewH = 0;
			#if DEBUG_WINDOW
				GUIStyle debugstyle = new GUIStyle();

				EditorGUILayout.BeginVertical(debugstyle);
				EditorGUILayout.LabelField("Id: " + windowId + " | Compute order: " + computeOrder);
				EditorGUILayout.LabelField("type: " + GetType());
				EditorGUILayout.LabelField("Dependencies:");
				foreach (var dep in depencendies)
					EditorGUILayout.LabelField("    " + dep.windowId + " : " + dep.anchorId);
				EditorGUILayout.LabelField("Links:");
				foreach (var l in links)
					EditorGUILayout.LabelField("    " + l.distantWindowId + " : " + l.distantAnchorId);
				EditorGUILayout.EndVertical();
				debugViewH = (int)GUILayoutUtility.GetLastRect().height + 6; //add the padding and margin
			#endif

			GUILayout.BeginVertical();
			{
				RectOffset savedmargin = GUI.skin.label.margin;
				GUI.skin.label.margin = new RectOffset(2, 2, 5, 7);

				//magic to fit the window padding ???
				GUIStyle centerEverything = new GUIStyle();
				centerEverything.alignment = TextAnchor.MiddleCenter;
				GUILayout.Label(" ", centerEverything);
				GUILayout.Space(-GUI.skin.label.lineHeight);

				OnNodeGUI();

				EditorGUIUtility.labelWidth = 0;
				GUI.skin.label.margin = savedmargin;
			}
			GUILayout.EndVertical();

			int viewH = (int)GUILayoutUtility.GetLastRect().height;
			if (e.type == EventType.Repaint)
				viewHeight = viewH + debugViewH;

			viewHeight = Mathf.Max(viewHeight, maxAnchorRenderHeight);
				
			if (e.type == EventType.Repaint)
				viewHeight += 24;
			
			ProcessAnchors();
			RenderAnchors();

			Rect selectRect = new Rect(10, 18, windowRect.width - 20, windowRect.height - 18);
			if (e.type == EventType.MouseDown && e.button == 0 && selectRect.Contains(e.mousePosition))
				selected = !selected;
		}
		#endif

		public virtual void	OnNodeGUI()
		{
			EditorGUILayout.LabelField("empty node");
		}

		public void Process()
		{
			foreach (var kp in propertyDatas)
				if (kp.Value.mirroredField != null)
				{
					var val = kp.Value.anchorInstance;
					if (propertyDatas.ContainsKey(kp.Value.mirroredField))
					{
						var mirroredProp = propertyDatas[kp.Value.mirroredField];
						bakedNodeFields[mirroredProp.fieldName].SetValue(this, val);
					}
				}
				
			//send anchor connection events:
			if (lastAttachedLink != null)
			{
				OnNodeAnchorLink(lastAttachedLink.first, lastAttachedLink.second);
				lastAttachedLink = null;
			}

			OnNodeProcess();
		}

	#endregion

	#region Anchor rendering and processing

		void ProcessAnchor(
			PWAnchorData data,
			PWAnchorData.PWAnchorMultiData singleAnchor,
			ref Rect inputAnchorRect,
			ref Rect outputAnchorRect,
			ref PWAnchorInfo ret,
			int index = -1)
		{
			Rect anchorRect = (data.anchorType == PWAnchorType.Input) ? inputAnchorRect : outputAnchorRect;

			singleAnchor.anchorRect = anchorRect;

			if (!ret.mouseAbove)
			{
				ret = new PWAnchorInfo(data.fieldName, PWUtils.DecalRect(singleAnchor.anchorRect, graphDecal + windowRect.position),
					singleAnchor.color, data.type,
					data.anchorType, windowId, singleAnchor.id,
					data.classAQName, index,
					data.generic, data.allowedTypes,
					singleAnchor.linkType, singleAnchor.linkCount);
			}
			if (anchorRect.Contains(Event.current.mousePosition))
				ret.mouseAbove = true;
		}

		void ProcessAnchors()
		{
			PWAnchorInfo ret = new PWAnchorInfo();
			
			int		anchorWidth = 13;
			int		anchorHeight = 13;
			int		anchorMargin = 2;

			Rect	inputAnchorRect = new Rect(3, 20 + anchorMargin, anchorWidth, anchorHeight);
			Rect	outputAnchorRect = new Rect(windowRect.size.x - anchorWidth - 3, 20 + anchorMargin, anchorWidth, anchorHeight);

			//if there is more values in PWValues than the available anchor count, create new anchors:
			ForeachPWAnchorDatas((data) => {
				if (data.multiple && data.anchorInstance != null)
				{
					if (((PWValues)data.anchorInstance).Count >= data.multi.Count)
						data.AddNewAnchor(data.fieldName.GetHashCode() + data.multi.Count, false);
				}
			});

			ForeachPWAnchors((data, singleAnchor, i) => {
				//process anchor event and calcul rect position if visible
				if (singleAnchor.visibility != PWVisibility.Gone)
				{
					if (singleAnchor.visibility != PWVisibility.Gone && i <= 0)
					{
						if (data.anchorType == PWAnchorType.Input)
							inputAnchorRect.position += data.offset;
						else if (data.anchorType == PWAnchorType.Output)
							outputAnchorRect.position += data.offset;
					}
					if (singleAnchor.visibility == PWVisibility.Visible)
						ProcessAnchor(data, singleAnchor, ref inputAnchorRect, ref outputAnchorRect, ref ret, i);
					if (singleAnchor.visibility != PWVisibility.Gone)
					{
						if (data.anchorType == PWAnchorType.Input)
							inputAnchorRect.position += Vector2.up * (18 + anchorMargin);
						else if (data.anchorType == PWAnchorType.Output)
							outputAnchorRect.position += Vector2.up * (18 + anchorMargin);
					}
				}
			});
			maxAnchorRenderHeight = (int)Mathf.Max(inputAnchorRect.position.y - 20, outputAnchorRect.position.y - 20);
			anchorUnderMouse = ret;
		}

		public PWAnchorInfo GetAnchorUnderMouse()
		{
			return anchorUnderMouse;
		}
		
		void RenderAnchor(PWAnchorData data, PWAnchorData.PWAnchorMultiData singleAnchor, int index)
		{
			//if anchor have not been processed:
			if (singleAnchor == null || boxAnchorStyle == null)
				return ;

			string anchorName = (data.multiple) ? "" : data.anchorName;

			if (data.multiple)
			{
				if (singleAnchor.additional)
					anchorName = "+";
				else
					anchorName += index;
			}

			switch (singleAnchor.highlighMode)
			{
				case PWAnchorHighlight.AttachAdd:
					GUI.color = anchorAttachAddColor;
					break ;
				case PWAnchorHighlight.AttachNew:
					GUI.color = anchorAttachNewColor;
					break ;
				case PWAnchorHighlight.AttachReplace:
					GUI.color = anchorAttachReplaceColor;
					break ;
				case PWAnchorHighlight.None:
				default:
					GUI.color = singleAnchor.color;
					break ;

			}
			GUI.DrawTexture(singleAnchor.anchorRect, anchorTexture, ScaleMode.ScaleToFit);

			if (!String.IsNullOrEmpty(anchorName))
			{
				Rect	anchorNameRect = singleAnchor.anchorRect;
				Vector2 textSize = GUI.skin.label.CalcSize(new GUIContent(anchorName));
				if (data.anchorType == PWAnchorType.Input)
					anchorNameRect.position += new Vector2(-6, -3);
				else
					anchorNameRect.position += new Vector2(-textSize.x - 6, -3);
				anchorNameRect.size = textSize + new Vector2(25, 0); //add the anchorLabel size
				GUI.depth = 10;
				GUI.Label(anchorNameRect, anchorName, (data.anchorType == PWAnchorType.Input) ? inputAnchorLabelStyle : outputAnchorLabelStyle);
			}
			GUI.color = Color.white;
			
			if (!singleAnchor.enabled)
				GUI.DrawTexture(singleAnchor.anchorRect, anchorDisabledTexture);

			//reset the Highlight:
			singleAnchor.highlighMode = PWAnchorHighlight.None;

			if (data.required && singleAnchor.linkCount == 0
				&& (!data.multiple || (data.multiple && index < data.minMultipleValues)))
			{
				Rect errorIconRect = new Rect(singleAnchor.anchorRect);
				errorIconRect.size = Vector2.one * 17;
				errorIconRect.position += new Vector2(-6, -10);
				GUI.color = Color.red;
				GUI.DrawTexture(errorIconRect, errorIcon);
				GUI.color = Color.white;
			}

			#if DEBUG_WINDOW
				Rect anchorSideRect = singleAnchor.anchorRect;
				if (data.anchorType == PWAnchorType.Input)
				{
					anchorSideRect.size += new Vector2(100, 20);
				}
				else
				{
					anchorSideRect.position -= new Vector2(100, 0);
					anchorSideRect.size += new Vector2(100, 20);
				}
				GUI.Label(anchorSideRect, (long)singleAnchor.id + " | " + singleAnchor.linkCount);
			#endif
		}
		
		public void RenderAnchors()
		{
			var e = Event.current;

			//rendering anchors
			ForeachPWAnchors((data, singleAnchor, i) => {
				//draw anchor:
				if (singleAnchor.visibility != PWVisibility.Gone)
				{
					if (singleAnchor.visibility == PWVisibility.Visible)
						RenderAnchor(data, singleAnchor, i);
					if (singleAnchor.visibility == PWVisibility.InvisibleWhenLinking)
						singleAnchor.visibility = PWVisibility.Visible;
				}
			});
			
			//rendering node rename field	
			if (renamable)
			{
				Vector2	winSize = windowRect.size;
				Rect	renameRect = new Rect(0, 0, winSize.x, 18);
				Rect	renameIconRect = new Rect(winSize.x - 28, 3, 12, 12);
				string	renameNodeField = "renameWindow";

				GUI.color = Color.black * .9f;
				GUI.DrawTexture(renameIconRect, editIcon);
				GUI.color = Color.white;

				if (windowNameEdit)
				{
					GUI.SetNextControlName(renameNodeField);
					externalName = GUI.TextField(renameRect, externalName, renameNodeTextFieldStyle);
	
					if (e.type == EventType.MouseDown && !renameRect.Contains(e.mousePosition))
					{
						windowNameEdit = false;
						GUI.FocusControl(null);
					}
					if (GUI.GetNameOfFocusedControl() == renameNodeField)
					{
						if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.Escape)
						{
							windowNameEdit = false;
							GUI.FocusControl(null);
							e.Use();
						}
					}
				}
				
				if (renameIconRect.Contains(e.mousePosition))
				{
					if (e.type == EventType.Used) //used by drag
					{
						windowNameEdit = true;
						GUI.FocusControl(renameNodeField);
						var te = (TextEditor)GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl);
						if (te != null)
							te.SelectAll();
					}
					else if (e.type == EventType.MouseDown)
						windowNameEdit = false;
				}
			}
		}

	#endregion
	
	#region Links management and utils

		public List< PWLink > GetLinks()
		{
			return links;
		}

		public List< PWLink > GetLinks(int anchorId, int targetWindowId, int targetAnchorId)
		{
			return links.Where(l => l.localAnchorId == anchorId
				&& l.distantWindowId == targetWindowId
				&& l.distantAnchorId == targetAnchorId).ToList();
		}

		PWLinkType		GetLinkType(Type from, Type to)
		{
			if (from == typeof(Sampler2D) || to == typeof(Sampler2D))
				return PWLinkType.Sampler2D;
			if (from == typeof(Sampler3D) || to == typeof(Sampler3D))
				return PWLinkType.Sampler3D;
			if (from == typeof(float) || to == typeof(float))
				return PWLinkType.BasicData;
			if (from.IsSubclassOf(typeof(ChunkData)) || to.IsSubclassOf(typeof(ChunkData)))
				return PWLinkType.ChunkData;
			if (from == typeof(Vector2) || to == typeof(Vector2))
				return PWLinkType.TwoChannel;
			if (from == typeof(Vector3) || to == typeof(Vector3))
				return PWLinkType.ThreeChannel;
			if (from == typeof(Vector4) || to == typeof(Vector4))
				return PWLinkType.FourChannel;
			return PWLinkType.BasicData;
		}

		public void		AttachLink(PWAnchorInfo from, PWAnchorInfo to)
		{
			//quit if types are not compatible
			if (!AnchorAreAssignable(from, to))
				return ;
			if (from.anchorType == to.anchorType)
				return ;
			if (from.windowId == to.windowId)
				return ;

			//we store output links:
			if (from.anchorType == PWAnchorType.Output)
			{
				outputHasChanged = true;
				links.Add(new PWLink(
					to.windowId, to.anchorId, to.name, to.classAQName, to.propIndex,
					from.windowId, from.anchorId, from.name, from.classAQName, from.propIndex, GetAnchorDominantColor(from, to),
					GetLinkType(from.fieldType, to.fieldType))
				);
				lastAttachedLink = new Pair< string, int>(from.name, from.propIndex);
				//mark local output anchors as linked:
				ForeachPWAnchors((data, singleAnchor, i) => {
					if (singleAnchor.id == from.anchorId)
						singleAnchor.linkCount++;
				});
			}
			else //input links are stored as depencencies:
			{
				inputHasChanged = true;
				ForeachPWAnchors((data, singleAnchor, i) => {
					if (singleAnchor.id == from.anchorId)
					{
						lastAttachedLink = new Pair< string, int>(from.name, from.propIndex);
						singleAnchor.linkCount++;
						//if data was added to multi-anchor:
						if (data.multiple && data.anchorInstance != null)
						{
							if (i == data.multipleValueCount)
								data.AddNewAnchor(data.fieldName.GetHashCode() + i + 1);
						}
						if (data.mirroredField != null)
						{
							//no need to check if anchorInstance is null because is is assigned from mirrored property.
							var mirroredProp = propertyDatas[data.mirroredField];
							if ((Type)mirroredProp.type == typeof(PWValues))
								mirroredProp.AddNewAnchor(mirroredProp.fieldName.GetHashCode() + i + 1);
						}
					}
				});
				depencendies.Add(new PWNodeDependency(to.windowId, to.anchorId, from.anchorId));
			}
		}

		public void AttachLink(string myAnchor, PWNode target, string targetAnchor)
		{
			if (!propertyDatas.ContainsKey(myAnchor) || !target.propertyDatas.ContainsKey(targetAnchor))
			{
				Debug.LogWarning("property not found: \"" + targetAnchor + "\" in " + target);
				return ;
			}

			PWAnchorData fromAnchor = propertyDatas[myAnchor];
			PWAnchorData toAnchor = target.propertyDatas[targetAnchor];

			PWAnchorInfo from = new PWAnchorInfo(
					fromAnchor.fieldName, new Rect(), fromAnchor.first.color,
					fromAnchor.type, fromAnchor.anchorType, fromAnchor.windowId,
					fromAnchor.first.id, fromAnchor.classAQName,
					(fromAnchor.multiple) ? 0 : -1, fromAnchor.generic, fromAnchor.allowedTypes,
					fromAnchor.first.linkType, fromAnchor.first.linkCount
			);
			PWAnchorInfo to = new PWAnchorInfo(
				toAnchor.fieldName, new Rect(), toAnchor.first.color,
				toAnchor.type, toAnchor.anchorType, toAnchor.windowId,
				toAnchor.first.id, toAnchor.classAQName,
				(toAnchor.multiple) ? 0 : -1, toAnchor.generic, toAnchor.allowedTypes,
				toAnchor.first.linkType, toAnchor.first.linkCount
			);

			AttachLink(from, to);
		}
		
		public void		DeleteAllLinkOnAnchor(int anchorId)
		{
			links.RemoveAll(l => {
				bool delete = l.localAnchorId == anchorId;
				if (delete)
					OnNodeAnchorUnlink(l.localName, l.localIndex);
				return delete;
			});
			if (DeleteDependencies(d => d.connectedAnchorId == anchorId) == 0)
			{
				PWAnchorData.PWAnchorMultiData singleAnchorData;
				GetAnchorData(anchorId, out singleAnchorData);
				singleAnchorData.linkCount = 0;
			}
		}

		public void		DeleteLink(int myAnchorId, PWNode distantWindow, int distantAnchorId)
		{
			links.RemoveAll(l => {
				bool delete = l.localAnchorId == myAnchorId && l.distantWindowId == distantWindow.windowId && l.distantAnchorId == distantAnchorId;
				if (delete)
					OnNodeAnchorUnlink(l.localName, l.localIndex);
				return delete;
			});
			//delete dependency and if it's not a dependency, decrement the linkCount of the link.
			if (DeleteDependencies(d => d.windowId == distantWindow.windowId && d.connectedAnchorId == myAnchorId && d.anchorId == distantAnchorId) == 0)
			{
				PWAnchorData.PWAnchorMultiData singleAnchorData;
				GetAnchorData(myAnchorId, out singleAnchorData);
				if (singleAnchorData != null)
					singleAnchorData.linkCount--;
			}
		}
		
		public void		DeleteLinkByWindowTarget(int targetWindowId)
		{
			PWAnchorData.PWAnchorMultiData singleAnchorData;
			for (int i = 0; i < links.Count; i++)
				if (links[i].distantWindowId == targetWindowId)
				{
					OnNodeAnchorUnlink(links[i].localName, links[i].localIndex);
					GetAnchorData(links[i].localAnchorId, out singleAnchorData);
					singleAnchorData.linkCount--;
					links.RemoveAt(i--);
				}
		}
		
		public void		DeleteAllLinks(bool callback = true)
		{
			if (callback)
				foreach (var l in links)
					OnNodeAnchorUnlink(l.localName, l.localIndex);
			links.Clear();
			depencendies.Clear();

			ForeachPWAnchors((data, singleAnchor, i) => {
				singleAnchor.linkCount = 0;
			});
		}

	#endregion

	#region dependencies management and utils

		int DeleteDependencies(Func< PWNodeDependency, bool > pred)
		{
			PWAnchorData.PWAnchorMultiData	singleAnchor;
			PWAnchorData.PWAnchorMultiData	multiAnchor;
			PWAnchorData					data;
			int								index;
			int								nDeleted = 0;

			depencendies.RemoveAll(d => {
				bool delete = pred(d);
				if (delete)
				{
					data = GetAnchorData(d.connectedAnchorId, out singleAnchor, out index);
					if (data == null)
						return delete;
					singleAnchor.linkCount--;
					nDeleted++;
					if (data.multiple)
					{
						PWValues vals = bakedNodeFields[data.fieldName].GetValue(this) as PWValues;
						vals.AssignAt(index, null, null);
						for (int i = vals.Count - 1; i != 0 && i >= data.minMultipleValues ; i--)
						{
							int id = data.fieldName.GetHashCode() + i;
							if (GetAnchorData(id, out multiAnchor) != null && multiAnchor.linkCount == 0)
							{
								vals.RemoveAt(i);
								data.multi.RemoveAt(i);
								data.multipleValueCount--;
								if (GetAnchorData(id + 1, out multiAnchor) != null)
									multiAnchor.id--;
							}
							else if (GetAnchorData(id, out multiAnchor) != null)
								break ;
						}
					}
					else
						bakedNodeFields[data.fieldName].SetValue(this, null);
				}
				return delete;
			});
			return nDeleted;
		}

		public void		DeleteDependency(int targetWindowId, int distantAnchorId)
		{
			DeleteDependencies(d => d.windowId == targetWindowId && d.anchorId == distantAnchorId);
		}

		public void		DeleteDependenciesByWindowTarget(int targetWindowId)
		{
			DeleteDependencies(d => d.windowId == targetWindowId);
		}
		
		public List< Pair < int, int > >	GetAnchorConnections(int anchorId)
		{
			return depencendies.Where(d => d.connectedAnchorId == anchorId)
					.Select(d => new Pair< int, int >(d.windowId, d.anchorId))
					.Concat(links.Where(l => l.localAnchorId == anchorId)
						.Select(l => new Pair< int, int >(l.distantWindowId, l.distantAnchorId))
					).ToList();
		}

		public List< PWNodeDependency >	GetDependencies()
		{
			return depencendies;
		}

		public List< PWNodeDependency > GetDependencies(int anchorId)
		{
			return depencendies.Where(d => d.connectedAnchorId == anchorId).ToList();
		}

	#endregion

	#region Editor utils

		static bool			AnchorAreAssignable(Type fromType, PWAnchorType fromAnchorType, bool fromGeneric, SerializableType[] fromAllowedTypes, PWAnchorInfo to, bool verbose = false)
		{
			bool ret = false;

			if ((fromType != typeof(PWValues) && to.fieldType != typeof(PWValues)) //exclude PWValues to simple assignation (we need to check with allowedTypes)
				&& (fromType.IsAssignableFrom(to.fieldType) || fromType == typeof(object) || to.fieldType == typeof(object)))
			{
				if (verbose)
					Debug.Log(fromType.ToString() + " is assignable from " + to.fieldType.ToString());
				return true;
			}

			if (fromGeneric || to.generic)
			{
				if (verbose)
					Debug.Log("from type is generic");
				SerializableType[] types = (fromGeneric) ? fromAllowedTypes : to.allowedTypes;
				Type secondType = (fromGeneric) ? to.fieldType : fromType;
				foreach (Type firstT in types)
					if (fromGeneric && to.generic)
					{
						if (verbose)
							Debug.Log("to type is generic");
						foreach (Type toT in to.allowedTypes)
						{
							if (verbose)
								Debug.Log("checking assignable from " + firstT + " to " + toT);
							if (firstT.IsAssignableFrom(toT))
								ret = true;
						}
					}
					else
					{
						if (verbose)
							Debug.Log("checking assignable from " + firstT + " to " + secondType);
						if (firstT.IsAssignableFrom(secondType))
							ret = true;
					}
			}
			else
			{
				if (verbose)
					Debug.Log("non-generic types, checking assignable from " + fromType + " to " + to.fieldType);
				if (fromType.IsAssignableFrom(to.fieldType) || to.fieldType.IsAssignableFrom(fromType))
					ret = true;
			}
			if (verbose)
				Debug.Log("result: " + ret);
			return ret;
		}

		public static bool		AnchorAreAssignable(PWAnchorInfo from, PWAnchorInfo to, bool verbose = false)
		{
			if (from.anchorType == to.anchorType)
				return false;
			return AnchorAreAssignable(from.fieldType, from.anchorType, from.generic, from.allowedTypes, to, verbose);
		}

		public void		HighlightLinkableAnchorsTo(PWAnchorInfo toLink)
		{
			PWAnchorType anchorType = InverAnchorType(toLink.anchorType);

			ForeachPWAnchors((data, singleAnchor, i) => {
				//Hide anchors and highlight when mouse hover
				// Debug.Log(data.windowId + ":" + data.fieldName + ": " + AnchorAreAssignable(data.type, data.anchorType, data.generic, data.allowedTypes, toLink, true));
				if (data.windowId != toLink.windowId
					&& data.anchorType == anchorType
					&& AnchorAreAssignable(data.type, data.anchorType, data.generic, data.allowedTypes, toLink, false))
				{
					if (data.multiple)
					{
						//display additional anchor to attach on next rendering
						data.displayHiddenMultipleAnchors = true;
					}
					if (singleAnchor.anchorRect.Contains(Event.current.mousePosition))
						if (singleAnchor.visibility == PWVisibility.Visible)
						{
							singleAnchor.highlighMode = PWAnchorHighlight.AttachNew;
							if (singleAnchor.linkCount > 0)
							{
								//if anchor is locked:
								if (data.anchorType == PWAnchorType.Input)
									singleAnchor.highlighMode = PWAnchorHighlight.AttachReplace;
								else
									singleAnchor.highlighMode = PWAnchorHighlight.AttachAdd;
							}
						}
				}
				else if (singleAnchor.visibility == PWVisibility.Visible
					&& singleAnchor.id != toLink.anchorId
					&& singleAnchor.linkCount == 0)
					singleAnchor.visibility = PWVisibility.InvisibleWhenLinking;
			});
		}

		public void BeginFrameUpdate()
		{
			if (oldSeed != seed)
				seedHasChanged = true;
			if (oldChunkPosition != chunkPosition)
				positionHasChanged = true;
			if (oldChunkSize != chunkSize)
				chunkSizeHasChanged = true;
			if (oldStep != step)
				stepHasChanged = true;
		}

		public void		EndFrameUpdate()
		{
			//reset values at the end of the frame
			oldSeed = seed;
			oldChunkPosition = chunkPosition;
			oldChunkSize = chunkSize;
			oldStep = step;
			seedHasChanged = false;
			positionHasChanged = false;
			chunkSizeHasChanged = false;
			inputHasChanged = false;
			outputHasChanged = false;
			reloadRequested = false;
			justReloaded = false;
			stepHasChanged = false;
		}
		
		public void		DisplayHiddenMultipleAnchors(bool display = true)
		{
			ForeachPWAnchorDatas((data)=> {
				if (data.multiple)
					data.displayHiddenMultipleAnchors = display;
			});
		}

		public bool		WindowShouldClose()
		{
			return windowShouldClose;
		}

		public bool		CheckRequiredAnchorLink()
		{
			bool	ret = true;

			ForeachPWAnchors((data, singleAnchor, i) => {
			if (data.required && singleAnchor.linkCount == 0
					&& (!data.multiple || (data.multiple && i < data.minMultipleValues)))
				ret = false;
			});
			return ret;
		}

		public void		SetWindowId(int id)
		{
			windowId = id;
			ForeachPWAnchors((data, singleAnchor, i) => {
				data.windowId = id;
			});
		}

		public void RunNodeAwake()
		{
			OnNodeAwake();
			OnNodeCreate();
			unserializeInitialized = true;
		}

		public void UpdateGraphDecal(Vector2 graphDecal)
		{
			this.graphDecal = graphDecal;
		}

		public void OnClickedOutside()
		{
			if (Event.current.button == 0)
			{
				windowNameEdit = false;
				GUI.FocusControl(null);
			}
			if (Event.current.button == 0 && !Event.current.shift)
				selected = false;
			isDragged = false;
		}

		public void AnchorBeingLinked(int anchorId)
		{
			ForeachPWAnchors((data, singleAnchor, i) => {
				if (singleAnchor.id == anchorId)
				{
					if (singleAnchor.linkCount == 0)
						singleAnchor.highlighMode = PWAnchorHighlight.AttachNew;
					else
						if (data.anchorType == PWAnchorType.Input)
							singleAnchor.highlighMode = PWAnchorHighlight.AttachReplace;
						else
							singleAnchor.highlighMode = PWAnchorHighlight.AttachAdd;
				}
			});
		}

	#endregion

	#region Node property / anchor API

		/* Utils function to manipulate PWnode variables */

		public void		UpdatePropEnabled(string propertyName, bool enabled, int index = 0)
		{
			if (propertyDatas.ContainsKey(propertyName))
			{
				var anchors = propertyDatas[propertyName].multi;
				if (anchors.Count <= index)
					return ;
				anchors[index].enabled = true;
			}
		}

		public void		UpdatePropName(string propertyName, string newName, int index = 0)
		{
			if (propertyDatas.ContainsKey(propertyName))
			{
				var anchors = propertyDatas[propertyName].multi;
				if (anchors.Count <= index)
					return ;
				anchors[index].name = newName;
			}
		}

		public void		UpdatePropBackgroundColor(string propertyName, Color newColor, int index = 0)
		{
			if (propertyDatas.ContainsKey(propertyName))
			{
				var anchors = propertyDatas[propertyName].multi;
				if (anchors.Count <= index)
					return ;
				anchors[index].color = (SerializableColor)newColor;
			}
		}

		public void		UpdatePropVisibility(string propertyName, PWVisibility visibility, int index = 0)
		{
			if (propertyDatas.ContainsKey(propertyName))
			{
				var anchors = propertyDatas[propertyName].multi;
				if (anchors.Count <= index)
					return ;
				anchors[index].visibility = visibility;
			}
		}

		public int		GetPropLinkCount(string propertyName, int index = 0)
		{
			if (propertyDatas.ContainsKey(propertyName))
			{
				var anchors = propertyDatas[propertyName].multi;
				if (anchors.Count <= index)
					return -1;
				return anchors[index].linkCount;
			}
			return -1;
		}

		public Rect?	GetAnchorRect(string propertyName, int index = 0)
		{
			if (propertyDatas.ContainsKey(propertyName))
			{
				var anchors = propertyDatas[propertyName].multi;
				if (anchors.Count <= index)
					return null;
				return PWUtils.DecalRect(anchors[index].anchorRect, graphDecal + windowRect.position);
			}
			return null;
		}

		public Rect?	GetAnchorRect(int id)
		{
			var matches =	from p in propertyDatas
							from p2 in p.Value.multi
							where p2.id == id
							select p2;

			if (matches.Count() == 0)
				return null;
			Rect r = matches.First().anchorRect;
			return PWUtils.DecalRect(r, graphDecal + windowRect.position);
		}

		public PWAnchorData	GetAnchorData(string propName)
		{
			if (propertyDatas.ContainsKey(propName))
				return propertyDatas[propName];
			return null;
		}

	#endregion

	#region Unused (for the moment) overrided functions
		public void OnDestroy()
		{
			// Debug.Log("node " + nodeTypeName + " detroyed !");
		}

		public void OnGUI()
		{
			EditorGUILayout.LabelField("You are on the wrong window !");
		}
		
		public void OnInspectorGUI()
		{
			EditorGUILayout.LabelField("nope !");
		}
	#endregion

	#region Utils and Miscellaneous

		void ForeachPWAnchors(Action< PWAnchorData, PWAnchorData.PWAnchorMultiData, int > callback, bool showAdditional = false)
		{
			foreach (var PWAnchorData in propertyDatas)
			{
				var data = PWAnchorData.Value;
				if (data.multiple)
				{
					if (data.anchorInstance == null)
					{
						data.anchorInstance = bakedNodeFields[data.fieldName].GetValue(this);
						if (data.anchorInstance == null)
							continue ;
						else
							data.multipleValueCount = (data.anchorInstance as PWValues).Count;
					}

					int anchorCount = Mathf.Max(data.minMultipleValues, ((PWValues)data.anchorInstance).Count);
					if (data.anchorType == PWAnchorType.Input)
						if (data.displayHiddenMultipleAnchors || showAdditional)
							anchorCount++;
					for (int i = 0; i < anchorCount; i++)
					{
						//if multi-anchor instance does not exists, create it:
						if (data.displayHiddenMultipleAnchors && i == anchorCount - 1)
							data.multi[i].additional = true;
						else
							data.multi[i].additional = false;
						callback(data, data.multi[i], i);
					}
				}
				else
					callback(data, data.first, -1);
			}
		}

		void ForeachPWAnchorDatas(Action< PWAnchorData > callback)
		{
			foreach (var data in propertyDatas)
			{
				if (data.Value != null)
					callback(data.Value);
			}
		}

		public static PWLinkType GetLinkTypeFromType(Type fieldType)
		{
			if (fieldType == typeof(Sampler2D))
				return PWLinkType.Sampler2D;
			if (fieldType == typeof(Sampler3D))
				return PWLinkType.Sampler3D;
			if (fieldType == typeof(Vector3) || fieldType == typeof(Vector3i))
				return PWLinkType.ThreeChannel;
			if (fieldType == typeof(Vector4))
				return PWLinkType.FourChannel;
			return PWLinkType.BasicData;
		}

		PWAnchorType	InverAnchorType(PWAnchorType type)
		{
			if (type == PWAnchorType.Input)
				return PWAnchorType.Output;
			else if (type == PWAnchorType.Output)
				return PWAnchorType.Input;
			return PWAnchorType.None;
		}

		public PWAnchorData	GetAnchorData(int id, out PWAnchorData.PWAnchorMultiData singleAnchorData)
		{
			int				index;
			
			return GetAnchorData(id, out singleAnchorData, out index);
		}

		public PWAnchorData	GetAnchorData(int id, out PWAnchorData.PWAnchorMultiData singleAnchorData, out int index)
		{
			PWAnchorData					ret = null;
			PWAnchorData.PWAnchorMultiData	s = null;
			int								retIndex = 0;

			ForeachPWAnchors((data, singleAnchor, i) => {
				if (singleAnchor.id == id)
				{
					s = singleAnchor;
					ret = data;
					retIndex = i;
				}
			}, true);
			index = retIndex;
			singleAnchorData = s;
			return ret;
		}

		Color GetAnchorDominantColor(PWAnchorInfo from, PWAnchorInfo to)
		{
			if (from.anchorColor == PWColorPalette.GetColor("greyAnchor") || from.anchorColor == PWColorPalette.GetColor("whiteAnchor"))
				return to.anchorColor;
			if (to.anchorColor == PWColorPalette.GetColor("greyAnchor") || to.anchorColor == PWColorPalette.GetColor("whiteAnchor"))
				return from.anchorColor;
			return to.anchorColor;
		}

	#endregion

    }
}