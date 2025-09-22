#region Usings

// #region Usings: Specifies the namespaces that the class depends on.
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

#endregion

namespace ScratchCardGenerator.Services
{
    /// <summary>
    /// Provides services for creating a manifest file with checksums for data integrity verification.
    /// The manifest contains a cryptographic hash for each key data file, which can be used by an external tool
    /// to prove that the files have not been altered or corrupted since their creation.
    /// </summary>
    public class ManifestCreationService
    {
        #region Public Methods

        /// <summary>
        /// Asynchronously creates a manifest file containing the SHA-256 hash of each file in the provided list.
        /// </summary>
        /// <param name="filePaths">An enumerable collection of full paths to the files to be included in the manifest.</param>
        /// <param name="manifestPath">The full path where the manifest file will be saved.</param>
        public async Task CreateManifestFileAsync(IEnumerable<string> filePaths, string manifestPath)
        {
            var manifestContent = new StringBuilder();

            // Iterate through each provided file path to compute its hash.
            foreach (var filePath in filePaths)
            {
                if (File.Exists(filePath))
                {
                    // Asynchronously compute the hash to avoid blocking the thread on file I/O for large files.
                    string hash = await ComputeSha256HashAsync(filePath);

                    // Append the entry to the manifest content in the standard format: "{hash} *{filename}".
                    // The asterisk is a common convention in checksum files.
                    manifestContent.AppendLine($"{hash} *{Path.GetFileName(filePath)}");
                }
            }

            // Asynchronously write the complete manifest content to the specified file.
            await File.WriteAllTextAsync(manifestPath, manifestContent.ToString(), Encoding.UTF8);
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Asynchronously computes the SHA-256 hash of a file.
        /// </summary>
        /// <param name="filePath">The path to the file to hash.</param>
        /// <returns>A string representing the lowercase hexadecimal SHA-256 hash.</returns>
        private async Task<string> ComputeSha256HashAsync(string filePath)
        {
            // The 'using' statements ensure that the cryptographic service and the file stream
            // are properly disposed of, releasing system resources even if an error occurs.
            using (var sha256 = SHA256.Create())
            {
                using (var stream = File.OpenRead(filePath))
                {
                    byte[] hashBytes = await sha256.ComputeHashAsync(stream);
                    return ToHexString(hashBytes);
                }
            }
        }

        /// <summary>
        /// Converts a byte array to its lowercase hexadecimal string representation.
        /// </summary>
        /// <param name="bytes">The byte array to convert.</param>
        /// <returns>A hexadecimal string.</returns>
        private string ToHexString(byte[] bytes)
        {
            // Using a StringBuilder is more efficient for string concatenation in a loop
            // than using the '+' operator.
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes)
            {
                // Append each byte as a two-character lowercase hexadecimal string ("x2").
                sb.Append(b.ToString("x2"));
            }
            return sb.ToString();
        }

        #endregion
    }
}