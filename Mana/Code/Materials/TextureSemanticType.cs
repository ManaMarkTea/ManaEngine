namespace Mana.Code.Materials
{
    /// <summary>
    /// Defines the purpose of a texture.
    /// </summary>
    public enum TextureSemanticType : int
    {
        /// <summary>
        /// No texture, but the value can be used as a 'texture semantic'.
        /// </summary>
        None = 0x0,

        /// <summary>
        /// A diffuse texture that is combined with the result of the diffuse lighting equation.
        /// </summary>
        Diffuse = 0x1,

        /// <summary>
        /// A specular texture that is combined with the result of the specular lighting equation.
        /// </summary>
        Specular = 0x2,

        /// <summary>
        /// An ambient texture that is combined with the ambient lighting equation.
        /// </summary>
        Ambient = 0x3,

        /// <summary>
        /// An emissive texture that is added to the result of the lighting calculation. It is not influenced
        /// by incoming light, instead it represents the light that the object is naturally emitting.
        /// </summary>
        Emissive = 0x4,

        /// <summary>
        /// A height map texture. by convention, higher gray-scale values stand for
        /// higher elevations from some base height.
        /// </summary>
        Height = 0x5,

        /// <summary>
        /// A tangent-space normal map. There are several conventions for normal maps
        /// and Assimp does (intentionally) not distinguish here.
        /// </summary>
        Normals = 0x6,

        /// <summary>
        /// A texture that defines the glossiness of the material. This is the exponent of the specular (phong)
        /// lighting equation. Usually there is a conversion function defined to map the linear color values
        /// in the texture to a suitable exponent.
        /// </summary>
        Shininess = 0x7,

        /// <summary>
        /// The texture defines per-pixel opacity. usually 'white' means opaque and 'black' means 'transparency. Or quite
        /// the opposite.
        /// </summary>
        Opacity = 0x8,

        /// <summary>
        /// A displacement texture. The exact purpose and format is application-dependent. Higher color values stand for higher vertex displacements.
        /// </summary>
        Displacement = 0x9,

        /// <summary>
        /// A lightmap texture (aka Ambient occlusion). Both 'lightmaps' and dedicated 'ambient occlusion maps' are covered by this material property. The
        /// texture contains a scaling value for the final color value of a pixel. Its intensity is not affected by incoming light.
        /// </summary>
        Lightmap = 0xA,

        /// <summary>
        /// A reflection texture. Contains the color of a perfect mirror reflection. This is rarely used, almost never for real-time applications.
        /// </summary>
        Reflection = 0xB,

        /// <summary>
        /// An unknown texture that does not mention any of the defined texture type definitions. It is still imported, but is excluded from any
        /// further postprocessing.
        /// </summary>
        Unknown = 0xC
    }
}
