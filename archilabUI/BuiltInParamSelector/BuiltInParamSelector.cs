﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Xml;
using archilabUI.Utilities;
using Autodesk.DesignScript.Runtime;
using Dynamo.Engine;
using Dynamo.Graph;
using Dynamo.Graph.Nodes;
using ProtoCore.AST.AssociativeAST;
using RevitServices.Persistence;

namespace archilabUI.BuiltInParamSelector
{
    [NodeCategory("archilab.Revit.Parameter")]
    [NodeDescription("Allows you to select a BuiltInParameter name for use with GetBuiltInParameter node.")]
    [NodeName("Get BipParameter Name")]
    [InPortDescriptions("Input element.")]
    [InPortNames("Element")]
    [InPortTypes("var")]
    [OutPortDescriptions("Name of the BuiltInParameter selected.")]
    [OutPortNames("bipName")]
    [OutPortTypes("var")]
    [IsDesignScriptCompatible]
    internal class BuiltInParamSelector : NodeModel
    {
        #region Properties

        public event Action RequestChangeBuiltInParamSelector;
        internal EngineController EngineController { get; set; }

        private ObservableCollection<ParameterWrapper> _itemsCollection;
        public ObservableCollection<ParameterWrapper> ItemsCollection
        {
            get { return _itemsCollection; }
            set { _itemsCollection = value; RaisePropertyChanged("ItemsCollection"); }
        }

        private ParameterWrapper _selectedItem;
        public ParameterWrapper SelectedItem
        {
            get { return _selectedItem; }
            set
            {
                _selectedItem = value;
                RaisePropertyChanged("SelectedItem");
                OnNodeModified(true);
            }
        }

        #endregion

        public BuiltInParamSelector()
        {
            RegisterAllPorts();
            foreach (var current in InPorts)
            {
                current.Connectors.CollectionChanged += Connectors_CollectionChanged;
            }
            ItemsCollection = new ObservableCollection<ParameterWrapper>();
        }

        #region UI Methods

        protected virtual void OnRequestChangeBuiltInParamSelector()
        {
            RequestChangeBuiltInParamSelector?.Invoke();
        }

        private void Connectors_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Remove)
            {
                ItemsCollection.Clear();
                SelectedItem = null;
                OnRequestChangeBuiltInParamSelector();
            }
            else
            {
                OnRequestChangeBuiltInParamSelector();
            }
        }

        private List<ParameterWrapper> GetParameters(Autodesk.Revit.DB.Element e, string prefix)
        {
            var items = new List<ParameterWrapper>();
            foreach (Autodesk.Revit.DB.Parameter p in e.Parameters)
            {
                if (p.StorageType != Autodesk.Revit.DB.StorageType.None)
                {
                    var idef = p.Definition as Autodesk.Revit.DB.InternalDefinition;
                    if (idef == null) continue;

                    var bipName = idef.BuiltInParameter.ToString();
                    items.Add(new ParameterWrapper { Name = prefix + " | " + p.Definition.Name, BipName = bipName });
                }
            }
            return items;
        }

        private Autodesk.Revit.DB.Element GetInputElement()
        {
            Autodesk.Revit.DB.Element e = null;
            if (!HasConnectedInput(0)) return null;

            var owner = InPorts[0].Connectors[0].Start.Owner;
            var index = InPorts[0].Connectors[0].Start.Index;
            var name = owner.GetAstIdentifierForOutputIndex(index).Name;
            var mirror = EngineController.GetMirror(name);
            if (!mirror.GetData().IsCollection)
            {
                var element = (Revit.Elements.Element)mirror.GetData().Data;
                if (element != null)
                {
                    e = element.InternalElement;
                }
            }
            return e;
        }

        public void PopulateItems()
        {
            var e = GetInputElement();
            if (e != null)
            {
                var items = new List<ParameterWrapper>();

                // add instance parameters
                items.AddRange(GetParameters(e, "Instance"));

                // add type parameters
                if (e.CanHaveTypeAssigned())
                {
                    var et = DocumentManager.Instance.CurrentDBDocument.GetElement(e.GetTypeId());
                    if (et != null)
                    {
                        items.AddRange(GetParameters(et, "Type"));
                    }
                }
                ItemsCollection = new ObservableCollection<ParameterWrapper>(items.OrderBy(x => x.Name));
            }

            if (SelectedItem == null)
            {
                SelectedItem = ItemsCollection.FirstOrDefault();
            }
        }

        #endregion

        #region Node Serialization/Deserialization

        private string SerializeValue()
        {
            if (SelectedItem != null)
            {
                return SelectedItem.Name + "+" + SelectedItem.BipName;
            }
            return string.Empty;
        }

        private ParameterWrapper DeserializeValue(string val)
        {
            try
            {
                var name = val.Split('+')[0];
                var bipName = val.Split('+')[1];
                return SelectedItem = new ParameterWrapper { Name = name, BipName = bipName };
            }
            catch
            {
                return SelectedItem = new ParameterWrapper { Name = "None", BipName = "None" };
            }
        }

        protected override void SerializeCore(XmlElement nodeElement, SaveContext context)
        {
            base.SerializeCore(nodeElement, context);

            if (nodeElement.OwnerDocument == null) return;

            var wrapper = nodeElement.OwnerDocument.CreateElement("paramWrapper");
            wrapper.InnerText = SerializeValue();
            nodeElement.AppendChild(wrapper);
        }

        protected override void DeserializeCore(XmlElement nodeElement, SaveContext context)
        {
            base.DeserializeCore(nodeElement, context);

            var colorNode = nodeElement.ChildNodes.Cast<XmlNode>().FirstOrDefault(x => x.Name == "paramWrapper");
            if (colorNode == null) return;

            var deserialized = DeserializeValue(colorNode.InnerText);
            if (deserialized.Name == "None" && deserialized.BipName == "None")
            {
                return;
            }
            SelectedItem = deserialized;
        }

        #endregion

        [IsVisibleInDynamoLibrary(false)]
        public override IEnumerable<AssociativeNode> BuildOutputAst(List<AssociativeNode> inputAstNodes)
        {
            var list = new List<AssociativeNode>();
            if (!HasConnectedInput(0) || SelectedItem == null)
            {
                list.Add(AstFactory.BuildAssignment(GetAstIdentifierForOutputIndex(0), AstFactory.BuildNullNode()));
            }
            else
            {
                AssociativeNode associativeNode = AstFactory.BuildStringNode(SelectedItem.BipName);
                list.Add(AstFactory.BuildAssignment(GetAstIdentifierForOutputIndex(0), associativeNode));
            }
            return list;
        }
    }
}
