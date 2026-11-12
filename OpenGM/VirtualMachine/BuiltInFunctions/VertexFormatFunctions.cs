using OpenGM.IO;

namespace OpenGM.VirtualMachine.BuiltInFunctions
{
    public static class VertexFormatFunctions
    {
        public static VertexFormat? NewFormat;

        /// <summary>
        /// Every format vertex_format_end has handed out, by the id it returned. A format outlives
        /// the call that built it because vertex_begin takes one, so it cannot just be discarded at
        /// vertex_format_end the way it used to be.
        /// </summary>
        public static readonly Dictionary<int, VertexFormat> Formats = new();

        /// <summary>
        /// Ids start at 1, not 0: GML treats 0 as false, and a game storing a format in a variable
        /// it later tests for truthiness would silently take the "no format" branch.
        /// </summary>
        private static int _nextFormatId = 1;

        [GMLFunction("vertex_format_begin")]
        public static object? vertex_format_begin(object?[] args)
        {
            if (NewFormat != null)
            {
                throw new Exception("Already creating format!");
            }

            NewFormat = new();
            return null;
        }

        [GMLFunction("vertex_format_end")]
        public static object? vertex_format_end(object?[] args)
        {
            if (NewFormat == null)
            {
                DebugLog.LogWarning("vertex_format_end with no format under construction.");
                return -1;
            }

            var id = _nextFormatId++;
            Formats[id] = NewFormat;
            NewFormat = null;
            return id;
        }

        [GMLFunction("vertex_format_delete")]
        public static object? vertex_format_delete(object?[] args)
        {
            Formats.Remove(args[0].Conv<int>());
            return null;
        }

        [GMLFunction("vertex_format_add_position")]
        public static object? vertex_format_add_position(object?[] args)
        {
            if (NewFormat == null)
            {
                throw new Exception("No format is under construction!");
            }

            NewFormat.AddPosition();
            return null;
        }

        [GMLFunction("vertex_format_add_position_3d")]
        public static object? vertex_format_add_position_3d(object?[] args)
        {
            if (NewFormat == null)
            {
                throw new Exception("No format is under construction!");
            }

            NewFormat.AddPosition3D();
            return null;
        }

        [GMLFunction("vertex_format_add_color")]
        [GMLFunction("vertex_format_add_colour")]
        public static object? vertex_format_add_colour(object?[] args)
        {
            if (NewFormat == null)
            {
                throw new Exception("No format is under construction!");
            }

            NewFormat.AddColor();
            return null;
        }

        [GMLFunction("vertex_format_add_normal")]
        public static object? vertex_format_add_normal(object?[] args)
        {
            if (NewFormat == null)
            {
                throw new Exception("No format is under construction!");
            }

            NewFormat.AddNormal();
            return null;
        }

        [GMLFunction("vertex_format_add_textcoord")]
        [GMLFunction("vertex_format_add_texcoord")]
        public static object? vertex_format_add_texcoord(object?[] args)
        {
            if (NewFormat == null)
            {
                throw new Exception("No format is under construction!");
            }

            NewFormat.AddTexcoord();
            return null;
        }

        // vertex_format_add_custom
    }
}
