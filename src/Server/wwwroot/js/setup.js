/**
 * Server admin shell bootstrap.
 * The server UI no longer has a project-selection setup flow.
 */

import { $, api } from './utils.js';
import { enterApp } from './app.js';

export async function initSetup() {
  try {
    const status = await api('/status');
    enterApp(`知识库 · ${status.moduleCount} 个模块`);
  } catch (e) {
    $('statusText').textContent = '连接失败: ' + e.message;
  }
}
