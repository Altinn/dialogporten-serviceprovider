using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;

namespace Digdir.BDB.Dialogporten.ServiceProvider.Controllers;

[ApiController]
[Route("fce")]
[Authorize(AuthenticationSchemes = "DialogToken")]
[EnableCors("AllowedOriginsPolicy")]
public class FrontChannelEmbedController : ControllerBase
{
    [HttpGet]
    public IActionResult Get([FromQuery] bool html = false)
    {
        var sb = new StringBuilder();
        foreach (var claim in User.Claims)
        {
            sb.AppendLine($"* {claim.Type}: {claim.Value}");
        }

        return html ? HtmlContent(sb.ToString()) : MarkdownContent(sb.ToString());


    }

    private IActionResult MarkdownContent(string claims)
    {
        return Content(
            $"""
             # Hello from FrontChannelEmbed

             This is a paragraph with some text, using Markdown. [Here is a link](https://www.example.com). Here is some additional text.

             * Item 1
             * Item 2
             * Item 3

             ## Subsection

             This is some more text. Lorem ipsum dolor sit amet, consectetur adipiscing elit sed do eiusmod tempor 
             incididunt ut labore et dolore magna aliqua.

             ## Another subsection

             Lorem ipsum dolor sit amet, consectetur adipiscing elit sed do eiusmod tempor incididunt ut labore et 
             dolore

             ## Dialog token

             {claims}
             """, "text/markdown");
    }

    private IActionResult HtmlContent(string claims)
    {
        return Content(
            $"""
              <h1>Hello from FrontChannelEmbed</h1>
              <p>This is a paragraph with some text. <a href='https://www.example.com'>Here is a link</a>. Here is some additional text.</p>
              <ul>
                  <li>Item 1</li>
                  <li>Item 2</li>
                  <li>Item 3</li>
              </ul>
              <h2>Subsection</h2>
              <p>This is some more text. Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.</p>
              <h2>Another subsection</h2>
              <p>Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.</p>
              <h2>Dialog token</h2>
              <pre>{claims}</pre>
              """, "text/html");
    }
}
