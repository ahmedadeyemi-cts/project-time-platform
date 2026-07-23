import fs from 'node:fs';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';

const repoRoot = fileURLToPath(new URL('../../../../', import.meta.url));
const read = (relativePath) => fs.readFileSync(path.join(repoRoot, relativePath), 'utf8');

const frontend = read('src/frontend/project-time-web/src/ProjectWorkspaceCenter.jsx');
const css = read('src/frontend/project-time-web/src/project-workspace-center.css');
const backend = read('src/backend/ProjectTime.Api/Modules/ProjectWorkspaceModule.cs');
const docker = read('deployment/containers/web/Dockerfile');
const pkg = JSON.parse(read('src/frontend/project-time-web/package.json'));

let checks = 0;
let failures = 0;

function test(name, condition) {
  checks += 1;
  if (!condition) failures += 1;
  console.log(`PROJECT_WORKSPACE_DOCUMENT_DOWNLOAD_${name}=${condition ? 'PASSED' : 'FAILED'}`);
}

test(
  'AUTHENTICATED_FETCH',
  frontend.includes('fetch(workspaceDocument.downloadUrl')
    && frontend.includes('headers: getProjectPulseAuthHeaders(selectedViewAsUserId)')
);
test(
  'NO_UNAUTHENTICATED_ANCHOR_NAVIGATION',
  !frontend.includes('href={selectedViewAsUserId ? `${document.downloadUrl}')
    && frontend.includes('onClick={() => downloadDocument(document)}')
);
test(
  'BLOB_DOWNLOAD',
  frontend.includes('response.blob()')
    && frontend.includes('URL.createObjectURL(blob)')
    && frontend.includes('anchor.download = readDownloadFileName')
    && frontend.includes('URL.revokeObjectURL(blobUrl)')
);
test(
  'SERVER_ERROR_SURFACED',
  frontend.includes('readDownloadError(response)')
    && frontend.includes('result?.message || result?.status')
    && frontend.includes('workspace-download-status')
);
test(
  'VIEW_AS_HEADER_PRESERVED',
  frontend.includes("headers['X-ProjectPulse-View-As-User'] = viewAsUserId")
    && frontend.includes('getProjectPulseAuthHeaders(selectedViewAsUserId)')
);
test(
  'BACKEND_ROUTE',
  backend.includes('app.MapGet("/api/project-workspace/documents/{documentId:guid}/download"')
    && backend.includes('DownloadDocumentAsync(Guid documentId, HttpContext httpContext)')
);
test(
  'BACKEND_SCOPE_AND_FILE_GUARDS',
  backend.includes('ResolveViewAsAccessContextAsync(connection, httpContext, actualAccess)')
    && backend.includes('d.project_intake_document_id = @document_id')
    && backend.includes('if (!File.Exists(storagePath))')
);
test(
  'BUTTON_STYLING',
  css.includes('.workspace-download-link:disabled')
    && css.includes('.workspace-download-status.error')
);
test(
  'CONTAINER_CONTEXT',
  docker.includes('COPY src/backend/ProjectTime.Api/Modules/ProjectWorkspaceModule.cs')
    && docker.includes('src/backend/ProjectTime.Api/Modules/ProjectWorkspaceModule.cs')
);
test(
  'BUILD_GATE',
  pkg.scripts?.['validate:project-workspace-download'] === 'node ./scripts/validate-project-workspace-document-download.mjs'
    && pkg.scripts?.build?.includes('npm run validate:project-workspace-download')
);

console.log(`PROJECT_WORKSPACE_DOCUMENT_DOWNLOAD_CHECKS=${checks}`);
console.log(`PROJECT_WORKSPACE_DOCUMENT_DOWNLOAD_CONTRACT=${failures ? 'FAILED' : 'PASSED'}`);
process.exitCode = failures ? 1 : 0;
