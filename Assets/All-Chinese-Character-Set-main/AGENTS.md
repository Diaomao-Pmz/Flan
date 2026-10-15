# Repository Instructions

## Project Shape

- `source/` 是字符集的权威源文件：
  - `source/3500.txt`
  - `source/7000.txt`
  - `source/all.txt`
  - `source/symbols.txt`
- 项目根目录下的字符集文本是生成产物：
  - `symbols.txt`
  - `symbols-fullwidth.txt`
  - `3500+symbols.txt`
  - `7000+symbols.txt`
  - `all+symbols.txt`
- `scripts/` 存放合并、去重、打包脚本。
- `xlsx/` 和 `Fonts/` 是附件资料，默认不要读取、扫描或修改。只有用户明确要求处理这些附件时才进入这两个目录。

## Editing Rules

- 修改字符集内容时，编辑 `source/` 下的源文件，再通过脚本重新生成根目录产物。
- 不要手动编辑根目录生成产物来修复字符集内容。
- 尽量保持字符顺序稳定。除非用户明确要求重排，不要排序、格式化、分段重写或引入大规模无关 diff。
- 字符文件应保持纯文本内容。避免加入注释、标题、空格说明或其他非字符集数据。
- 涉及新增字符时，先判断应该进入哪个源文件：常用字源、全集源，还是符号源。不要因为生成产物缺字符就直接改产物。
- 字符集源文件通常是超长单行文本。修改单个字符或短片段时，做带断言的最小范围替换；确认目标字符状态和替换片段唯一性，不要手动重写整行。

## Scripts

- 生成根目录产物：

  ```sh
  node scripts/build.js
  ```

  读取 `source/`，写入根目录生成产物。

- 合并新增全集字符：

  ```sh
  node scripts/merge.js
  ```

  依赖项目根目录的临时输入 `new.txt`，把尚不存在的字符追加到 `source/all.txt`。

- 源文件去重：

  ```sh
  node scripts/remove-duplicates.js
  ```

  会直接改写 `source/symbols.txt`、`source/3500.txt`、`source/7000.txt`。只有明确需要规范化源文件时才运行，并在运行后检查 diff。

## Verification

- 修改 `source/` 后，通常运行 `node scripts/build.js`，确认生成产物同步更新。
- 运行脚本后用 `git diff` 检查变化范围，确认没有误改 `xlsx/`、`Fonts/` 或无关文件。
- 如果改动涉及脚本逻辑，要用当前仓库的小样本或实际源文件验证输出，不要只凭阅读推断。
- 如果用临时脚本辅助修改字符文件，脚本必须限制为明确的单点变更，并在修改前做断言检查。

## Code Notes

- 本项目没有 package 配置时，直接使用 Node.js 执行 `scripts/*.js`，不要新增包管理器配置来包一层无必要命令。
- 如果修改去重逻辑，优先使用按 Unicode code point 迭代的写法，例如 `for...of` 或 `[...str]`，避免把代理对字符拆坏。
- 对构建产物的任何变化都应该能从 `source/` 和 `scripts/` 复现。
