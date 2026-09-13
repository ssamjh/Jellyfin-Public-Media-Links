#!/usr/bin/env node
/*
 * Adds a release to manifest.json, the file Jellyfin reads when you add this repo
 * as a plugin repository.
 *
 * The shape is dictated by MediaBrowser.Model.Updates.PackageInfo / VersionInfo, which
 * declare explicit camelCase JsonPropertyName values -- notably "guid" for the plugin id.
 * The checksum must be the MD5 of the zip, hex encoded; Jellyfin refuses to install if it
 * does not match.
 *
 * Usage:
 *   node build/update-manifest.js --version 1.0.0.0 --zip path/to.zip \
 *       --source-url https://.../plugin.zip [--changelog "..."] [--target-abi 12.0.0.0]
 */

const fs = require('fs');
const path = require('path');
const crypto = require('crypto');

const REPO = 'https://github.com/ssamjh/Jellyfin-Public-Media-Links';

const PLUGIN = {
    guid: '2b9c4f61-7a3d-4e58-9d1c-0f5a6c8e2b74',
    name: 'Public Media Links',
    description:
        'Creates signed, self-expiring URLs for a single library item. The link carries its own '
        + 'HMAC signature and expiry, so it can be handed to someone without a Jellyfin account '
        + 'and without exposing your access token. Links can be revoked individually or all at once.',
    overview: 'Share expiring, account-free direct play links to individual library items.',
    owner: 'ssamjh',
    category: 'General'
};

function arg(name, fallback) {
    const i = process.argv.indexOf('--' + name);
    if (i === -1) {
        if (fallback === undefined) {
            throw new Error(`Missing required argument --${name}`);
        }
        return fallback;
    }
    const value = process.argv[i + 1];
    if (value === undefined || value.startsWith('--')) {
        throw new Error(`Argument --${name} needs a value`);
    }
    return value;
}

/** Jellyfin parses this with System.Version, which needs 2-4 numeric parts. */
function normalizeVersion(raw) {
    const cleaned = raw.replace(/^v/, '');
    if (!/^\d+(\.\d+){1,3}$/.test(cleaned)) {
        throw new Error(`Version "${raw}" is not a valid System.Version`);
    }
    const parts = cleaned.split('.');
    while (parts.length < 4) {
        parts.push('0');
    }
    return parts.join('.');
}

function main() {
    const version = normalizeVersion(arg('version'));
    const zip = arg('zip');
    const sourceUrl = arg('source-url');
    const targetAbi = normalizeVersion(arg('target-abi', '12.0.0.0'));
    const changelog = arg('changelog', '');
    const manifestPath = path.resolve(arg('manifest', 'manifest.json'));

    const checksum = crypto.createHash('md5').update(fs.readFileSync(zip)).digest('hex');

    let manifest;
    if (fs.existsSync(manifestPath)) {
        manifest = JSON.parse(fs.readFileSync(manifestPath, 'utf8'));
    } else {
        manifest = [{ ...PLUGIN, versions: [] }];
    }

    let pkg = manifest.find(p => p.guid.toLowerCase() === PLUGIN.guid);
    if (!pkg) {
        pkg = { ...PLUGIN, versions: [] };
        manifest.push(pkg);
    }

    const entry = {
        version,
        changelog,
        targetAbi,
        sourceUrl,
        checksum,
        timestamp: new Date().toISOString().replace(/\.\d{3}Z$/, 'Z'),
        repositoryName: PLUGIN.name,
        repositoryUrl: REPO
    };

    // Replacing rather than appending keeps re-running a release idempotent.
    pkg.versions = pkg.versions.filter(v => v.version !== version);
    pkg.versions.unshift(entry);

    fs.writeFileSync(manifestPath, JSON.stringify(manifest, null, 2) + '\n');

    console.log(`Added ${PLUGIN.name} ${version} (abi ${targetAbi}, md5 ${checksum})`);
}

main();
