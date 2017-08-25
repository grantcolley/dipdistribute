﻿using Microsoft.AspNetCore.Http;
using System.IO;
using System.Threading.Tasks;

namespace DipDistributor.Middleware
{
    public class FileStreamMiddleware
    {
        public FileStreamMiddleware(RequestDelegate next)
        {
        }

        public async Task Invoke(HttpContext context)
        {
            string body;
            var stream = context.Request.Body;
            using (var reader = new StreamReader(stream))
            {
                body = await reader.ReadToEndAsync();
            }

            using (var fileStream = new FileStream(body, FileMode.Open, FileAccess.Read))
            {
                await fileStream.CopyToAsync(context.Response.Body);
            }
        }
    }
}
