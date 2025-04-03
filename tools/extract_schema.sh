#!/bin/bash

# Script to dereference and convert an OpenAPI specification to JSON Schema,
# then extract a specific schema from the JSON Schema file, and clean up temporary files.

# Usage: ./extract_schema.sh <OpenAPI_Spec_URL>

# Exit immediately if a command exits with a non-zero status
set -e

# Check if the correct number of arguments are provided
if [ "$#" -ne 1 ]; then
	echo "Usage: $0 <Dialogporten_OpenAPI_Spec_URL>"
	exit 1
fi

# Assign arguments to variables
OPENAPI_URL="$1"
SCHEMA_NAME="CreateDialogCommand"
TARGET_FILE="creates-a-new-dialog-POST-request-schema.json"

# Define filenames and directories
OPENAPI_FILE="openapi.json"
MODIFIED_OPENAPI_FILE="openapi_modified.json"
BUNDLED_OPENAPI_FILE="openapi_dereferenced.json"
FINAL_SCHEMA_DIR="json_schema"
FINAL_SCHEMA_FILE="schema.json"

# Check for required commands
for cmd in curl jq swagger-cli pip3 install openapi2jsonschema; do
	if ! command -v "$cmd" &>/dev/null; then
		echo "Error: '$cmd' command not found. Please install it before running this script."
		exit 1
	fi
done

# Step 1: Download the OpenAPI specification
echo "Downloading OpenAPI specification from $OPENAPI_URL..."
curl -sSL "$OPENAPI_URL" -o "$OPENAPI_FILE"

# Step 2: Remove the "subParties" property from "AuthorizedPartyDto"
echo "Removing 'subParties' property from 'AuthorizedPartyDto'..."
jq 'del(.components.schemas.V1EndUserPartiesQueriesGet_AuthorizedParty.properties.subParties)' "$OPENAPI_FILE" >"$MODIFIED_OPENAPI_FILE"

# Step 3: Dereference the modified OpenAPI specification
echo "Dereferencing OpenAPI specification..."
swagger-cli bundle --dereference "$MODIFIED_OPENAPI_FILE" --outfile "$BUNDLED_OPENAPI_FILE" --type json

echo "Dereferenced OpenAPI specification saved to $BUNDLED_OPENAPI_FILE."

# Step 4: Convert the entire dereferenced OpenAPI spec to JSON Schema and output to a directory
echo "Converting entire OpenAPI spec to JSON Schema..."
mkdir -p "$FINAL_SCHEMA_DIR"
openapi2jsonschema --output "$FINAL_SCHEMA_DIR" --input "$BUNDLED_OPENAPI_FILE" >/dev/null
mkdir -p test
node test.js "$BUNDLED_OPENAPI_FILE" test

echo "Converted JSON Schema files saved to $FINAL_SCHEMA_DIR."

# Step 5: Find the specific schema file for the specified component
find . -name "*$TARGET_FILE" -exec mv {} $FINAL_SCHEMA_FILE \;

# Step 6: Remove superfluous oneOfs

jq 'walk(if type == "object" and .oneOf then
        if (.oneOf | length) == 1 then
            .oneOf[0]
        else
            .
        end
    else
        .
    end)' $FINAL_SCHEMA_FILE >$FINAL_SCHEMA_FILE.tmp

echo "Schema '$SCHEMA_NAME' extracted and saved to $FINAL_SCHEMA_FILE."

# Step 7: Clean up temporary files and directory
echo "Cleaning up temporary files..."
rm -rf *.json $FINAL_SCHEMA_DIR
mv $FINAL_SCHEMA_FILE.tmp $FINAL_SCHEMA_FILE

echo "Process completed successfully."
