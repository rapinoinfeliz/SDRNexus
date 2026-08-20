import { readFile, readdir } from "node:fs/promises";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

import Ajv2020 from "ajv/dist/2020.js";
import addFormats from "ajv-formats";

const root = dirname(dirname(fileURLToPath(import.meta.url)));
const contractDirectory = join(root, "contracts", "sdr", "v1");
const exampleDirectory = join(contractDirectory, "examples");

const schemaFiles = (await readdir(contractDirectory))
  .filter((name) => name.endsWith(".schema.json"))
  .sort();

const schemas = await Promise.all(
  schemaFiles.map(async (name) => JSON.parse(await readFile(join(contractDirectory, name), "utf8"))),
);

const ajv = new Ajv2020({ allErrors: true, strict: true });
addFormats(ajv);
for (const schema of schemas) {
  ajv.addSchema(schema);
}

for (const schema of schemas) {
  ajv.getSchema(schema.$id);
}

const fixtures = [
  ["radio-snapshot.json", "radio-snapshot.schema.json"],
  ["candidate-request.json", "candidate-request.schema.json"],
  ["candidate-response.json", "candidate-response.schema.json"],
  ["pairing-start-request.json", "pairing.schema.json"],
  ["logbook-create.json", "logbook-create.schema.json"],
  ["wishlist-mutation.json", "wishlist-mutation.schema.json"],
  ["live-state.json", "live-state.schema.json"],
  ["live-tune-command.json", "live-command.schema.json"],
];

for (const [exampleName, schemaName] of fixtures) {
  const value = JSON.parse(await readFile(join(exampleDirectory, exampleName), "utf8"));
  const schema = schemas.find((candidate) => candidate.$id.endsWith(`/${schemaName}`));
  const validate = ajv.getSchema(schema.$id);
  if (!validate(value)) {
    throw new Error(`${exampleName} does not satisfy ${schemaName}:\n${ajv.errorsText(validate.errors, { separator: "\n" })}`);
  }
}

const openApi = JSON.parse(await readFile(join(contractDirectory, "openapi.json"), "utf8"));
if (openApi.openapi !== "3.1.0" || typeof openApi.paths !== "object") {
  throw new Error("openapi.json is not an OpenAPI 3.1 document");
}

function resolveJsonPointer(document, fragment) {
  if (!fragment || fragment === "#") return document;
  if (!fragment.startsWith("#/")) throw new Error(`Unsupported JSON pointer: ${fragment}`);
  return fragment
    .slice(2)
    .split("/")
    .map((part) => part.replaceAll("~1", "/").replaceAll("~0", "~"))
    .reduce((value, key) => value?.[key], document);
}

async function validateOpenApiReferences(value) {
  if (Array.isArray(value)) {
    for (const item of value) await validateOpenApiReferences(item);
    return;
  }
  if (!value || typeof value !== "object") return;

  if (typeof value.$ref === "string") {
    const hashIndex = value.$ref.indexOf("#");
    const fileName = hashIndex >= 0 ? value.$ref.slice(0, hashIndex) : value.$ref;
    const fragment = hashIndex >= 0 ? value.$ref.slice(hashIndex) : "#";
    const targetDocument = fileName
      ? JSON.parse(await readFile(join(contractDirectory, fileName), "utf8"))
      : openApi;
    if (resolveJsonPointer(targetDocument, fragment) === undefined) {
      throw new Error(`Unresolved OpenAPI reference: ${value.$ref}`);
    }
  }

  for (const child of Object.values(value)) await validateOpenApiReferences(child);
}

await validateOpenApiReferences(openApi);

console.log(`Validated ${schemas.length} schemas, ${fixtures.length} examples, and OpenAPI ${openApi.info.version}.`);
