#region Usings

// #region Usings: Specifies the namespaces that the class depends on.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

#endregion

namespace FinalGameChecker.Services
{
    /// <summary>
    /// Provides services to verify the integrity of data files using a manifest file containing SHA-256 hashes.
    /// This is a critical security component that ensures the data being checked has not been tampered with since generation.
    /// </summary>
    public class DataIntegrityService
    {
        #region Public Methods

        /// <summary>
        /// Asynchronously verifies a list of files against a given manifest file.
        /// </summary>
        /// <param name="manifestPath">The full path to the manifest file (e.g., data_manifest.sha256).</param>
        /// <param name="filesToVerify">A dictionary where the key is the filename (as it appears in the manifest) and the value is the full path to the file on disk.</param>
        /// <returns>
        /// A tuple containing:
        /// - bool Success: True if all files exist, are in the manifest, and have matching hashes.
        /// - List<string> MismatchedFiles: A list of filenames that failed verification, along with the reason for failure.
        /// </returns>
        public async Task<(bool Success, List<string> MismatchedFiles)> VerifyFilesAsync(string manifestPath, Dictionary<string, string> filesToVerify)
        {
            var mismatchedFiles = new List<string>();
            if (!File.Exists(manifestPath))
            {
                mismatchedFiles.Add("Manifest file not found");
                return (false, mismatchedFiles);
            }

            // Read all lines from the manifest file and parse them into a dictionary.
            // The key is the filename, and the value is the expected SHA-256 hash.
            var manifestHashes = (await File.ReadAllLinesAsync(manifestPath))
                .Select(line => line.Split(new[] { " *" }, StringSplitOptions.RemoveEmptyEntries)) // Split on the " *" delimiter.
                .Where(parts => parts.Length == 2) // Ensure the line is correctly formatted.
                .ToDictionary(parts => parts[1], parts => parts[0], StringComparer.OrdinalIgnoreCase); // Use case-insensitive comparison for filenames.

            // Iterate through each file that needs to be verified.
            foreach (var fileEntry in filesToVerify)
            {
                string fileName = fileEntry.Key;
                string filePath = fileEntry.Value;

                // Check 1: Does the file actually exist at the specified path?
                if (!File.Exists(filePath))
                {
                    mismatchedFiles.Add($"{fileName} (File not found at specified path)");
                    continue; // Move to the next file.
                }

                // Check 2: Is there an entry for this file in the manifest?
                if (!manifestHashes.TryGetValue(fileName, out var expectedHash))
                {
                    mismatchedFiles.Add($"{fileName} (Not found in manifest)");
                    continue;
                }

                // Check 3: Does the actual hash of the file on disk match the expected hash from the manifest?
                string actualHash = await ComputeSha256HashAsync(filePath);

                if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                {
                    mismatchedFiles.Add($"{fileName} (Hash mismatch)");
                }
            }

            // The verification is successful only if the list of mismatched files is empty.
            return (!mismatchedFiles.Any(), mismatchedFiles);
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
                    // Asynchronously compute the hash to avoid blocking the thread on file I/O for large files.
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
            // Using a StringBuilder is more efficient for string concatenation in a loop.
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
