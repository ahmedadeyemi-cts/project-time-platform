import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const repoRoot = fileURLToPath(new URL("../../../../", import.meta.url));
const repoPath = (relativePath) => path.join(repoRoot, relativePath);
const paths=["src/backend/ProjectTime.Api/Modules/DataGovernanceRetentionModule.cs","src/frontend/project-time-web/src/DataGovernanceRetentionCenter.jsx","src/frontend/project-time-web/src/data-governance-retention-center.css","docs/modules/module-079-data-governance-retention/README.md","docs/modules/module-079-data-governance-retention/API-CONTRACT.md","docs/modules/module-079-data-governance-retention/AUTHORIZATION-AND-SECURITY.md","docs/modules/module-079-data-governance-retention/OVERLAP-AND-RELEASE-GATES.md"];
let checks=0, failures=0; const test=(name,ok)=>{checks++;if(!ok)failures++;console.log(`MODULE_079_${name}=${ok?"PASSED":"FAILED"}`)};
for(const path of paths)test("FILE_"+path.split("/").pop().replace(/\W/g,"_").toUpperCase(),fs.existsSync(repoPath(path)));
const b=fs.readFileSync(repoPath(paths[0]),"utf8"),f=fs.readFileSync(repoPath(paths[1]),"utf8"),c=fs.readFileSync(repoPath(paths[2]),"utf8");
test("MAP_METHOD",b.includes("MapDataGovernanceRetentionEndpoints")); test("READ_SURFACES",["overview","domains","classifications","retention-policies","lineage","legal-holds","privacy-policy"].every(x=>b.includes(`"/${x}"`)));
test("LOCKED",b.includes("423Locked")&&b.includes("requestBodyRead = false")); test("ACTUAL_SESSION",b.includes("ProjectPulseActualUserId")); test("VIEW_AS_BLOCKED",b.includes("IsViewAs(context)"));
test("NO_HTTP_CLIENT",!b.includes("HttpClient")); test("NO_MUTATING_SQL",!/(INSERT|UPDATE|DELETE|MERGE)\s/i.test(b)); test("GET_ONLY_UI",f.includes("fetch(")&&!/method:\s*["\'](?:POST|PUT|PATCH|DELETE)/.test(f)); test("US_SIGNAL_BRAND",f.includes("ussignal.png")&&c.includes("#0077c8"));
const program=fs.readFileSync(repoPath("src/backend/ProjectTime.Api/Program.cs"),"utf8"),app=fs.readFileSync(repoPath("src/frontend/project-time-web/src/App.jsx"),"utf8"),pkg=fs.readFileSync(repoPath("src/frontend/project-time-web/package.json"),"utf8"); test("SHARED_RUNTIME_INTEGRATED",program.split("MapDataGovernanceRetentionEndpoints").length-1===1&&app.includes("import DataGovernanceRetentionCenter")&&app.includes("activeRoute === 'data-governance-retention'")&&app.includes("<DataGovernanceRetentionCenter")&&pkg.includes("validate:module079"));
console.log(`MODULE_079_VALIDATION_CHECKS=${checks}`); console.log("MODULE_079_PHASE=RUNTIME_REGISTERED_FAIL_CLOSED"); console.log(`MODULE_079_CONTRACT=${failures?"FAILED":"PASSED"}`); process.exitCode=failures?1:0;
