#!/usr/bin/env ruby
# frozen_string_literal: true

# Adds the @objc facade sources to a target in WebRTCiOSSDK.xcodeproj.
#
# Dropping a .swift file into the source directory is not enough: Xcode targets list their files
# explicitly in project.pbxproj, so a file that is not referenced there is simply not compiled.
# Editing the pbxproj by hand is what the xcodeproj gem exists to avoid.
#
# Usage: add-facade.rb <project.xcodeproj> <target> <file.swift> [file.swift ...]
#
# Idempotent: re-running against a project that already has the files is a no-op, so a cached
# checkout can be reused without accumulating duplicate build phase entries.

require 'xcodeproj'

project_path, target_name, *sources = ARGV

if project_path.nil? || target_name.nil? || sources.empty?
  abort("usage: #{File.basename($PROGRAM_NAME)} <project.xcodeproj> <target> <file.swift>...")
end

project = Xcodeproj::Project.open(project_path)

target = project.targets.find { |candidate| candidate.name == target_name }
abort("error: target '#{target_name}' not found in #{project_path}") if target.nil?

project_dir = Pathname.new(File.dirname(File.expand_path(project_path)))

# Files already compiled by the target, as absolute paths, so re-runs can skip them.
# map + compact rather than filter_map: macOS still ships Ruby 2.6, where filter_map does not exist.
existing = target.source_build_phase.files.map do |build_file|
  build_file.file_ref&.real_path&.to_s
end.compact

group = project.main_group['AntMediaNetFacade'] ||
        project.main_group.new_group('AntMediaNetFacade')

added = []
sources.each do |source|
  absolute = File.expand_path(source)
  abort("error: #{absolute} does not exist") unless File.exist?(absolute)

  next if existing.include?(absolute)

  relative = Pathname.new(absolute).relative_path_from(project_dir).to_s
  reference = group.new_reference(relative)
  target.add_file_references([reference])
  added << relative
end

project.save

if added.empty?
  puts "add-facade: #{target_name} already compiles the facade sources"
else
  puts "add-facade: added to #{target_name}: #{added.join(', ')}"
end
