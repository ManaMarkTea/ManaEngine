// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.


using MetaRenderer.Code.ImageLoaders;
using Silk.NET.Assimp;
using Silk.NET.OpenGL;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using static System.Net.Mime.MediaTypeNames;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Mana.Code;
using Mana.Code.Materials;
using Mana.Code.Shapes.Modifiers;

namespace Mana.Code.Shapes
{

    public class ManaMesh : ManaObject
    {
        public ManaMaterial[] Materials;
        protected Vector3 MinSize;
        protected Vector3 MaxSize;
        
        public ManaVertexBufferObject? VertexBufferObject { get; internal set; }

        public ManaDynamicGeometryContainer DynamicGeometryContainer { get; internal set; }

        public List<ManaMeshModifier> Modifiers;

        public ManaMesh(ManaContext context) : base(context)
        {
            Context = context;
            Modifiers = new List<ManaMeshModifier>();
            Materials = new ManaMaterial[0];
            DynamicGeometryContainer = new ManaDynamicGeometryContainer();
        }
        
        public void Draw(ManaSceneNode owner, MeshRendererComponent? meshRenderer)
        {
            if (VertexBufferObject != null)
            {
                ManaMaterial material = Materials[0];
                if (meshRenderer != null)
                {
                    material = meshRenderer.Material;
                }

                material.Bind(owner.WorldTransform, owner);

                VertexBufferObject.Bind();
                VertexBufferObject.DrawElements();
                VertexBufferObject.UnBind();

                material.UnBind();
            }
        }

        public void RenderDirectly()
        {
            if (VertexBufferObject != null)
            {
                VertexBufferObject.Bind();
                VertexBufferObject.DrawElements();
                VertexBufferObject.UnBind();
            }
        }

        public void SetSize(float minSize, float maxSize)
        {
            MinSize = Vector3.One * minSize;
            MaxSize = Vector3.One * maxSize;
        }

        public void SetSize(Vector3 minSize, Vector3 maxSize)
        {
            MinSize = minSize;
            MaxSize = maxSize;
        }

    }
}