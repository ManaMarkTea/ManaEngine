using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Mana.Code.ManaShaders
{
 

    public class ManaShaderCache : ManaObject
    {
        private static string HashCode(string inputString)
        {
            // Convert the input string to bytes
            byte[] inputBytes = Encoding.UTF8.GetBytes(inputString);

            // Create an instance of the SHA-256 hashing algorithm
            using (SHA256 sha256 = SHA256.Create())
            {
                // Compute the hash
                byte[] hashBytes = sha256.ComputeHash(inputBytes);

                // Convert the hash to a hexadecimal string
                string hashString = BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
                return hashString;
            }
        }

        private static Dictionary<ManaContext, ManaShaderCache> ContextManaShaderCache = new Dictionary<ManaContext, ManaShaderCache>();

        public static ManaShaderCache GetShaderCache(ManaContext context)
        {
            if (!ContextManaShaderCache.ContainsKey(context))
            {
                ContextManaShaderCache.Add(context, new ManaShaderCache(context));
            }

            return ContextManaShaderCache[context];
        }

        private Dictionary<string, ManaCompiledShader> AllCompiledShaders = new Dictionary<string, ManaCompiledShader>();

        public ManaShaderCache(ManaContext context) : base(context)
        {
        }

        public ManaCompiledShader GetCompiledShader(string vertSource, string fragSource, string geomSource = "")
        {
            string allCode = vertSource + fragSource + geomSource;
            string hash = HashCode(allCode);
            if (!AllCompiledShaders.ContainsKey(hash))
            {
                var shader = new ManaCompiledShader(Context);
                shader.LoadShaderSource(vertSource, fragSource, geomSource);
                AllCompiledShaders.Add(hash, shader);
            }

            return AllCompiledShaders[hash];
        }

    }
}
