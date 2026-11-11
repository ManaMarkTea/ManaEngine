// ManaEditor's FBX reader: ufbx loads the file in metres, Y up, and this flattens what the scene importer needs
// (nodes, triangulated meshes per material, materials, cameras, lights) into one little-endian buffer that
// ManaFbxConverter.cs turns into glTF. Format version in MANA_FBX_VERSION; keep the two in step.
#include <stdint.h>
#include <stdlib.h>
#include <string.h>
#include "ufbx/ufbx.h"

#if defined(_WIN32)
#define MANA_EXPORT __declspec(dllexport)
#else
#define MANA_EXPORT __attribute__((visibility("default")))
#endif

#define MANA_FBX_VERSION 1

typedef struct
{
    unsigned char *data;
    size_t size;
    size_t capacity;
    int failed;
} mana_buffer;

static void put(mana_buffer *buffer, const void *bytes, size_t count)
{
    if (buffer->failed)
    {
        return;
    }
    if (buffer->size + count > buffer->capacity)
    {
        size_t capacity = buffer->capacity * 2 + count + 4096;
        unsigned char *grown = (unsigned char *)realloc(buffer->data, capacity);
        if (grown == NULL)
        {
            buffer->failed = 1;
            return;
        }
        buffer->data = grown;
        buffer->capacity = capacity;
    }
    memcpy(buffer->data + buffer->size, bytes, count);
    buffer->size += count;
}

static void put_u32(mana_buffer *buffer, uint32_t value) { put(buffer, &value, 4); }
static void put_i32(mana_buffer *buffer, int32_t value) { put(buffer, &value, 4); }
static void put_f32(mana_buffer *buffer, double value) { float f = (float)value; put(buffer, &f, 4); }

static void put_string(mana_buffer *buffer, const char *text, size_t length)
{
    put_u32(buffer, (uint32_t)length);
    put(buffer, text, length);
}

static void put_texture_name(mana_buffer *buffer, const ufbx_material_map *map)
{
    // Only the file's name: the importer finds the pack's own copy by its stem.
    const ufbx_texture *texture = map->texture;
    const char *name = "";
    size_t length = 0;
    if (texture != NULL)
    {
        ufbx_string path = texture->relative_filename;
        if (path.length == 0)
        {
            path = texture->filename;
        }
        name = path.data;
        length = path.length;
        for (size_t i = 0; i < path.length; i++)
        {
            if (path.data[i] == '/' || path.data[i] == '\\')
            {
                name = path.data + i + 1;
                length = path.length - i - 1;
            }
        }
    }
    put_string(buffer, name, length);
}

static int32_t index_of(const void *element, uint32_t typed_id)
{
    if (element == NULL)
    {
        return -1;
    }
    return (int32_t)typed_id;
}

static void put_part(mana_buffer *buffer, const ufbx_mesh *mesh, const ufbx_mesh_part *part, uint32_t *triangle,
    float *positions, float *normals, float *uvs, uint32_t *indices)
{
    size_t corners = 0;
    for (size_t f = 0; f < part->face_indices.count; f++)
    {
        ufbx_face face = mesh->faces.data[part->face_indices.data[f]];
        uint32_t count = ufbx_triangulate_face(triangle, mesh->max_face_triangles * 3, mesh, face);
        for (uint32_t c = 0; c < count * 3; c++)
        {
            uint32_t index = triangle[c];
            ufbx_vec3 p = ufbx_get_vertex_vec3(&mesh->vertex_position, index);
            ufbx_vec3 n = { 0.0, 1.0, 0.0 };
            ufbx_vec2 uv = { 0.0, 0.0 };
            if (mesh->vertex_normal.exists)
            {
                n = ufbx_get_vertex_vec3(&mesh->vertex_normal, index);
            }
            if (mesh->vertex_uv.exists)
            {
                uv = ufbx_get_vertex_vec2(&mesh->vertex_uv, index);
            }
            positions[corners * 3 + 0] = (float)p.x;
            positions[corners * 3 + 1] = (float)p.y;
            positions[corners * 3 + 2] = (float)p.z;
            normals[corners * 3 + 0] = (float)n.x;
            normals[corners * 3 + 1] = (float)n.y;
            normals[corners * 3 + 2] = (float)n.z;
            // FBX's UV origin is the bottom left, glTF's the top left.
            uvs[corners * 2 + 0] = (float)uv.x;
            uvs[corners * 2 + 1] = (float)(1.0 - uv.y);
            corners++;
        }
    }

    ufbx_vertex_stream streams[3] = {
        { positions, corners, sizeof(float) * 3 },
        { normals, corners, sizeof(float) * 3 },
        { uvs, corners, sizeof(float) * 2 },
    };
    ufbx_error error;
    size_t vertices = ufbx_generate_indices(streams, 3, indices, corners, NULL, &error);
    if (error.type != UFBX_ERROR_NONE)
    {
        buffer->failed = 1;
        return;
    }

    int32_t material = -1;
    if (part->index < mesh->materials.count)
    {
        ufbx_material *used = mesh->materials.data[part->index];
        material = index_of(used, used != NULL ? used->typed_id : 0);
    }
    put_i32(buffer, material);
    put_u32(buffer, (uint32_t)vertices);
    put(buffer, positions, vertices * 12);
    put(buffer, normals, vertices * 12);
    put(buffer, uvs, vertices * 8);
    put_u32(buffer, (uint32_t)corners);
    put(buffer, indices, corners * 4);
}

static void put_mesh(mana_buffer *buffer, const ufbx_mesh *mesh)
{
    put_string(buffer, mesh->name.data, mesh->name.length);
    uint32_t parts = 0;
    for (size_t i = 0; i < mesh->material_parts.count; i++)
    {
        if (mesh->material_parts.data[i].num_triangles > 0)
        {
            parts++;
        }
    }
    put_u32(buffer, parts);

    size_t corners = mesh->num_triangles * 3;
    uint32_t *triangle = (uint32_t *)malloc(sizeof(uint32_t) * (mesh->max_face_triangles * 3 + 3));
    float *positions = (float *)malloc(sizeof(float) * 3 * (corners + 1));
    float *normals = (float *)malloc(sizeof(float) * 3 * (corners + 1));
    float *uvs = (float *)malloc(sizeof(float) * 2 * (corners + 1));
    uint32_t *indices = (uint32_t *)malloc(sizeof(uint32_t) * (corners + 1));
    if (triangle == NULL || positions == NULL || normals == NULL || uvs == NULL || indices == NULL)
    {
        buffer->failed = 1;
    }
    else
    {
        for (size_t i = 0; i < mesh->material_parts.count; i++)
        {
            if (mesh->material_parts.data[i].num_triangles > 0)
            {
                put_part(buffer, mesh, &mesh->material_parts.data[i], triangle, positions, normals, uvs, indices);
            }
        }
    }
    free(triangle);
    free(positions);
    free(normals);
    free(uvs);
    free(indices);
}

static void copy_error(char *error, int error_size, const char *text, size_t length)
{
    if (error == NULL || error_size <= 0)
    {
        return;
    }
    if (length >= (size_t)error_size)
    {
        length = (size_t)error_size - 1;
    }
    memcpy(error, text, length);
    error[length] = 0;
}

// Reads `path` (UTF-8). On success returns 1 with `*out` a buffer for mana_fbx_free; on failure 0 and a message.
MANA_EXPORT int mana_fbx_read(const char *path, unsigned char **out, size_t *out_size, char *error, int error_size)
{
    *out = NULL;
    *out_size = 0;

    ufbx_load_opts options;
    memset(&options, 0, sizeof(options));
    options.target_axes = ufbx_axes_right_handed_y_up;
    options.target_unit_meters = 1.0;
    // Units and axes baked into the vertices and translations, as FBX2glTF did: no conversion node above the scene.
    options.space_conversion = UFBX_SPACE_CONVERSION_MODIFY_GEOMETRY;
    options.geometry_transform_handling = UFBX_GEOMETRY_TRANSFORM_HANDLING_MODIFY_GEOMETRY;
    options.inherit_mode_handling = UFBX_INHERIT_MODE_HANDLING_COMPENSATE;
    // glTF's camera looks down -Z with +Y up. Lights keep their node's own axes (see ManaFbxConverter).
    options.target_camera_axes = ufbx_axes_right_handed_y_up;
    options.generate_missing_normals = true;
    options.ignore_animation = true;

    ufbx_error load_error;
    ufbx_scene *scene = ufbx_load_file(path, &options, &load_error);
    if (scene == NULL)
    {
        char message[1024];
        size_t length = ufbx_format_error(message, sizeof(message), &load_error);
        copy_error(error, error_size, message, length);
        return 0;
    }

    mana_buffer buffer = { NULL, 0, 0, 0 };
    put(&buffer, "MFBX", 4);
    put_u32(&buffer, MANA_FBX_VERSION);

    put_u32(&buffer, (uint32_t)scene->nodes.count);
    for (size_t i = 0; i < scene->nodes.count; i++)
    {
        const ufbx_node *node = scene->nodes.data[i];
        if (node->is_root)
        {
            put_string(&buffer, "RootNode", 8);
        }
        else
        {
            put_string(&buffer, node->name.data, node->name.length);
        }
        put_i32(&buffer, index_of(node->parent, node->parent != NULL ? node->parent->typed_id : 0));
        ufbx_transform t = node->local_transform;
        put_f32(&buffer, t.translation.x); put_f32(&buffer, t.translation.y); put_f32(&buffer, t.translation.z);
        put_f32(&buffer, t.rotation.x); put_f32(&buffer, t.rotation.y); put_f32(&buffer, t.rotation.z); put_f32(&buffer, t.rotation.w);
        put_f32(&buffer, t.scale.x); put_f32(&buffer, t.scale.y); put_f32(&buffer, t.scale.z);
        put_i32(&buffer, index_of(node->mesh, node->mesh != NULL ? node->mesh->typed_id : 0));
        put_i32(&buffer, index_of(node->camera, node->camera != NULL ? node->camera->typed_id : 0));
        put_i32(&buffer, index_of(node->light, node->light != NULL ? node->light->typed_id : 0));
    }

    put_u32(&buffer, (uint32_t)scene->meshes.count);
    for (size_t i = 0; i < scene->meshes.count; i++)
    {
        put_mesh(&buffer, scene->meshes.data[i]);
    }

    put_u32(&buffer, (uint32_t)scene->materials.count);
    for (size_t i = 0; i < scene->materials.count; i++)
    {
        const ufbx_material *material = scene->materials.data[i];
        put_string(&buffer, material->name.data, material->name.length);
        ufbx_vec4 colour = material->pbr.base_color.value_vec4;
        double factor = material->pbr.base_factor.has_value ? material->pbr.base_factor.value_real : 1.0;
        put_f32(&buffer, colour.x * factor); put_f32(&buffer, colour.y * factor); put_f32(&buffer, colour.z * factor);
        put_f32(&buffer, material->pbr.opacity.has_value ? material->pbr.opacity.value_real : 1.0);
        ufbx_material_map base = material->pbr.base_color;
        if (base.texture == NULL)
        {
            base = material->fbx.diffuse_color;
        }
        put_texture_name(&buffer, &base);
        ufbx_material_map normal = material->pbr.normal_map;
        if (normal.texture == NULL)
        {
            normal = material->fbx.normal_map;
        }
        if (normal.texture == NULL)
        {
            normal = material->fbx.bump;
        }
        put_texture_name(&buffer, &normal);
    }

    put_u32(&buffer, (uint32_t)scene->cameras.count);
    for (size_t i = 0; i < scene->cameras.count; i++)
    {
        const ufbx_camera *camera = scene->cameras.data[i];
        const double radians = 3.14159265358979323846 / 180.0;
        put_f32(&buffer, camera->field_of_view_deg.y * radians);
        put_f32(&buffer, camera->aspect_ratio);
        put_f32(&buffer, camera->near_plane);
        put_f32(&buffer, camera->far_plane);
    }

    put_u32(&buffer, (uint32_t)scene->lights.count);
    for (size_t i = 0; i < scene->lights.count; i++)
    {
        const ufbx_light *light = scene->lights.data[i];
        put_string(&buffer, light->name.data, light->name.length);
        int32_t type = 0;
        if (light->type == UFBX_LIGHT_DIRECTIONAL)
        {
            type = 1;
        }
        else if (light->type == UFBX_LIGHT_SPOT)
        {
            type = 2;
        }
        put_i32(&buffer, type);
        put_f32(&buffer, light->color.x); put_f32(&buffer, light->color.y); put_f32(&buffer, light->color.z);
        put_f32(&buffer, light->intensity);
    }

    ufbx_free_scene(scene);
    if (buffer.failed)
    {
        free(buffer.data);
        copy_error(error, error_size, "Out of memory, or a mesh could not be indexed.", 46);
        return 0;
    }
    *out = buffer.data;
    *out_size = buffer.size;
    return 1;
}

MANA_EXPORT void mana_fbx_free(unsigned char *data)
{
    free(data);
}
