#!/bin/bash

# Get swagger
curl https://localhost:7214/swagger/v1/swagger.json -o swagger.json

## Make output dir
#mkdir out

# Remove subParties
jq 'del(.components.schemas.V1EndUserPartiesQueriesGet_AuthorizedParty.properties.subParties)' swagger.json >swagger2.json

# Dereference swagger
swagger-cli bundle --dereference swagger2.json --outfile swagger3.json --type json

# Convert to Json Schema
node test.js swagger3.json out

# Remove superfluous oneOfs
for file in out/*.json; do
	echo "$file"
	jq 'walk(if type == "object" and .oneOf then
            if (.oneOf | length) == 1 then
                .oneOf[0]
            else
                .
            end
        else
            .
        end)' $file >$file.tmp
done


rm -rf out/*.json


for file in out/*.tmp
do
    newname=${file%.tmp}
    mv $file $newname
done

# Clean up