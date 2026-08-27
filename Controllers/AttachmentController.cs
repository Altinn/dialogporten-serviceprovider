using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace Digdir.BDB.Dialogporten.ServiceProvider.Controllers
{
    [ApiController]
    [Authorize]
    [Route("attachment/{filename}")]
    public class AttachmentController : ControllerBase
    {
        private static readonly Regex FilenameRegex = new Regex(@"^[\x20-\x7E]{1,100}$");

        [HttpGet]
        public IActionResult Get(string filename, [FromQuery] bool inline = false)
        {
            if (!FilenameRegex.IsMatch(filename))
            {
                return BadRequest("Invalid filename.");
            }

            var fileExtension = filename.Split('.').Last().ToLower();

            string? filePath;
            string? contentType;

            switch (fileExtension)
            {
                case "docx":
                    filePath = "Attachments/sample.docx";
                    contentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
                    break;
                case "pdf":
                    filePath = "Attachments/sample.pdf";
                    contentType = "application/pdf";
                    break;
                case "zip":
                    filePath = "Attachments/sample.zip";
                    contentType = "application/zip";
                    break;
                default:
                    return NotFound();
            }

            if (!System.IO.File.Exists(filePath))
            {
                return NotFound();
            }

            // Not interpolated: the header type escapes quotes in the filename.
            var contentDisposition = new ContentDispositionHeaderValue(inline ? "inline" : "attachment");
            contentDisposition.SetHttpFileName(filename);
            Response.Headers.ContentDisposition = contentDisposition.ToString();

            var fileBytes = System.IO.File.ReadAllBytes(filePath);
            return File(fileBytes, contentType);
        }
    }
}
