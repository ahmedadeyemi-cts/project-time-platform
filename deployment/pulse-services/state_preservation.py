"""Pure deployment-state checks. No credentials, cloud calls, or authority grants."""
import re

MANAGER = "pulse-services-reviewed-cutover"

def require(value, code):
    if not value:
        raise ValueError(code)

def deployment_identity(source, run):
    require(isinstance(source, str) and re.fullmatch(r"[0-9a-f]{40}", source), "invalid_source_identity")
    require(isinstance(run, str) and re.fullmatch(r"[1-9][0-9]{0,19}", run), "invalid_run_identity")

def revision_is_ready(resource, name, revision, images=None):
    """A previous ready revision is never evidence that this deployment is ready."""
    require(isinstance(resource, dict), "invalid_resource_state")
    require(isinstance(name, str) and isinstance(revision, str)
            and revision.startswith(name + "--") and len(revision) > len(name) + 2,
            "invalid_expected_revision")
    properties = resource.get("properties")
    require(isinstance(properties, dict), "invalid_resource_state")
    if properties.get("latestRevisionName") != revision or properties.get("latestReadyRevisionName") != revision:
        return False
    require(properties.get("provisioningState") != "Failed", "expected_revision_failed")
    if images is not None:
        require(isinstance(images, (list, tuple, set)) and images, "expected_images_required")
        containers = properties.get("template", {}).get("containers")
        require(isinstance(containers, list) and containers
                and all(isinstance(x, dict) and isinstance(x.get("image"), str) for x in containers),
                "invalid_container_inventory")
        require({x["image"] for x in containers} == set(images), "expected_images_changed")
    return True

def require_cleanup_ownership(resource, source, run, *, application):
    """Source SHA alone does not distinguish two deployments of the same code."""
    deployment_identity(source, run)
    require(isinstance(resource, dict), "cleanup_state_invalid")
    tags = resource.get("tags")
    require(isinstance(tags, dict) and tags.get("managedBy") == MANAGER
            and tags.get("source") == source and tags.get("deploymentRun") == run,
            "cleanup_run_ownership_mismatch")
    if application:
        properties = resource.get("properties", {})
        require(properties.get("template", {}).get("revisionSuffix") == "svc-" + run,
                "cleanup_revision_changed")
    return True

def require_new_service_names(names, desired):
    require(isinstance(names, (list, tuple, set)) and isinstance(desired, (list, tuple, set)),
            "invalid_service_inventory")
    require(not set(names).intersection(desired), "existing_service_requires_separate_upgrade")
