const fs = require("fs");
const {openapiSchemaToJsonSchema: toJsonSchema} = require("@openapi-contrib/openapi-schema-to-json-schema");

if (process.argv.length !== 4) {
    console.log("hei");
    console.log(process.argv.length)
    return 0;
}
process.argv.forEach(function (val, index, array) {
    console.log(index + ': ' + val);
});
const fileName = process.argv[2];
const outputFolder = process.argv[3];

// Load OpenAPI spec
const openapiSpec = JSON.parse(fs.readFileSync(fileName, "utf8"));

// Convert schemas while keeping operationId
const convertedSchemas = {};

Object.entries(openapiSpec.paths).forEach(([path, methods]) => {
    Object.entries(methods).forEach(([method, operation]) => {
        if (operation.requestBody) {
            const content = operation.requestBody.content;
            if (content["application/json"] && content["application/json"].schema) {
                convertedSchemas[operation.operationId] = {
                    operationId: operation.operationId, // Preserve operationId
                    schema: toJsonSchema(content["application/json"].schema, {
                        keepNotSupported: ["nullable"]
                    }),
                };
            }
        }

    });

    // convertedSchemas[path] = {test: "tes", aaaaa: toJsonSchema(methods)};
});
// Save result
for (const path in convertedSchemas) {
    fs.writeFileSync(outputFolder + "/" + path + ".json", JSON.stringify(convertedSchemas[path], null, 2));
}
console.log("Conversion complete. Check output.json");