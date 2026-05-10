using System;
using System.Collections.Generic;
using System.Linq;

namespace TuioDemo.Services
{
    public class LevelService
    {
        private static List<Level> _levels = new List<Level>();
        private static int _nextId = 1;

        public LevelService()
        {
            // Initialize with default levels
            if (_levels.Count == 0)
            {
                _levels.Add(new Level { Id = _nextId++, Name = "Level 1", Description = "Beginner", CreatedAt = DateTime.Now, IsDeleted = false, RowVersion = Guid.NewGuid().ToByteArray() });
                _levels.Add(new Level { Id = _nextId++, Name = "Level 2", Description = "Intermediate", CreatedAt = DateTime.Now, IsDeleted = false, RowVersion = Guid.NewGuid().ToByteArray() });
                _levels.Add(new Level { Id = _nextId++, Name = "Level 3", Description = "Advanced", CreatedAt = DateTime.Now, IsDeleted = false, RowVersion = Guid.NewGuid().ToByteArray() });
            }
        }

        public ServiceResponse<List<Level>> GetAllLevels()
        {
            var response = new ServiceResponse<List<Level>>();
            try
            {
                var levels = _levels.Where(l => !l.IsDeleted).OrderBy(l => l.Id).ToList();
                response.Data = levels;
                response.Message = $"Retrieved {levels.Count} levels";
            }
            catch (Exception ex)
            {
                response.Success = false;
                response.Message = "Error retrieving levels";
                response.Errors.Add(ex.Message);
            }
            return response;
        }

        public ServiceResponse<Level> CreateLevel(string name, string description = "")
        {
            var response = new ServiceResponse<Level>();
            try
            {
                if (string.IsNullOrWhiteSpace(name))
                    throw new ValidationException("Level name cannot be empty.");

                if (_levels.Any(l => l.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && !l.IsDeleted))
                    throw new ValidationException("Level already exists.");

                var newLevel = new Level
                {
                    Id = _nextId++,
                    Name = name,
                    Description = description,
                    CreatedAt = DateTime.Now,
                    IsDeleted = false,
                    RowVersion = Guid.NewGuid().ToByteArray()
                };

                _levels.Add(newLevel);
                response.Data = newLevel;
                response.Message = $"Level '{name}' created successfully with ID: {newLevel.Id}";
            }
            catch (ValidationException ex)
            {
                response.Success = false;
                response.Message = "Validation Error";
                response.Errors.Add(ex.Message);
            }
            catch (Exception ex)
            {
                response.Success = false;
                response.Message = "Error creating level";
                response.Errors.Add(ex.Message);
            }
            return response;
        }

        public ServiceResponse<bool> DeleteLevel(int id)
        {
            var response = new ServiceResponse<bool>();
            try
            {
                var level = _levels.FirstOrDefault(l => l.Id == id);
                if (level == null)
                {
                    response.Success = false;
                    response.Message = "Level not found";
                    return response;
                }

                level.IsDeleted = true;
                response.Data = true;
                response.Message = "Level deleted successfully";
            }
            catch (Exception ex)
            {
                response.Success = false;
                response.Message = "Error deleting level";
                response.Errors.Add(ex.Message);
            }
            return response;
        }

        public ServiceResponse<Level> GetLevelById(int id)
        {
            var response = new ServiceResponse<Level>();
            try
            {
                var level = _levels.FirstOrDefault(l => l.Id == id && !l.IsDeleted);
                if (level == null)
                {
                    response.Success = false;
                    response.Message = "Level not found";
                    return response;
                }

                response.Data = level;
                response.Message = "Level retrieved successfully";
            }
            catch (Exception ex)
            {
                response.Success = false;
                response.Message = "Error retrieving level";
                response.Errors.Add(ex.Message);
            }
            return response;
        }
    }
}