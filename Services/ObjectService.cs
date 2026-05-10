using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Runtime.Serialization.Json;

public class ObjectService
{
    private static List<Object> _objects = new List<Object>();
    private static int _nextId = 1;


    private static string DataDir => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
    private static string ObjectsFile => Path.Combine(DataDir, "objects.json");

    private static void EnsureStorage()
    {
        Directory.CreateDirectory(DataDir);
        if (!File.Exists(ObjectsFile))
            File.WriteAllText(ObjectsFile, "[]");
    }


    static ObjectService()
    {
        LoadFromDisk();
    }


    private static void LoadFromDisk()
    {
        EnsureStorage();
        try
        {
            using (var fs = File.OpenRead(ObjectsFile))
            {
                if (fs.Length == 0)
                {
                    _objects = new List<Object>();
                }
                else
                {
                    var ser = new DataContractJsonSerializer(typeof(List<Object>));
                    _objects = (List<Object>)ser.ReadObject(fs) ?? new List<Object>();
                }
            }
            _nextId = _objects.Count == 0 ? 1 : _objects.Max(o => o.Id) + 1;
        }
        catch
        {
            _objects = new List<Object>();
            _nextId = 1;
        }
    }

    private static void SaveToDisk()
    {
        EnsureStorage();
        using (var fs = File.Create(ObjectsFile))
        {
            var ser = new DataContractJsonSerializer(typeof(List<Object>));
            ser.WriteObject(fs, _objects);
        }
    }




    public ServiceResponse<Object> CreateObject(string objectName, int levelId, int symbolId, string imagePath = null, string soundPath = null)
    {
        var response = new ServiceResponse<Object>();
        try
        {
            if (string.IsNullOrWhiteSpace(objectName))
                throw new ValidationException("Object name cannot be empty.");

            if (_objects.Any(o => o.ObjectName.Equals(objectName, StringComparison.OrdinalIgnoreCase) && !o.IsDeleted))
                throw new ValidationException("Object already exists.");

            if (_objects.Any(o => o.LevelId == levelId && o.SymbolId == symbolId && !o.IsDeleted))
                throw new ValidationException("Symbol ID is already used in this level.");

            //// AUTO-ASSIGN SYMBOL ID: Find the highest SymbolId in the selected level and add 1
            //int nextSymbolId = 0;
            //var existingInLevel = _objects.Where(o => o.LevelId == levelId && !o.IsDeleted).ToList();

            //if (existingInLevel.Count > 0)
            //{
            //    nextSymbolId = existingInLevel.Max(o => o.SymbolId) + 1;
            //}

            var newObject = new Object { Id = _nextId++, ObjectName = objectName, ImagePath = imagePath, SoundPath = soundPath, LevelId = levelId, SymbolId = symbolId, CreatedAt = DateTime.Now, IsDeleted = false, RowVersion = Guid.NewGuid().ToByteArray() };

            _objects.Add(newObject);
            SaveToDisk();
            response.Data = newObject;
            response.Message = $"Object added successfully with Symbol ID: {symbolId}!";
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
            response.Message = "Error adding object";
            response.Errors.Add(ex.Message);
        }
        return response;
    }

    public ServiceResponse<List<Object>> GetObjects(string filter = null, int page = 1, int pageSize = 10, string sortBy = "ObjectName", bool sortAscending = true)
    {
        var response = new ServiceResponse<List<Object>>();
        try
        {
            var query = _objects.Where(o => !o.IsDeleted).AsQueryable();

            if (!string.IsNullOrWhiteSpace(filter))
                query = query.Where(o => o.ObjectName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0);

            switch ((sortBy ?? "").ToLower())
            {
                case "createdat":
                    query = sortAscending ? query.OrderBy(o => o.CreatedAt) : query.OrderByDescending(o => o.CreatedAt);
                    break;
                default:
                    query = sortAscending ? query.OrderBy(o => o.ObjectName) : query.OrderByDescending(o => o.ObjectName);
                    break;
            }

            int totalCount = query.Count();
            var pagedObjects = query.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            response.Data = pagedObjects;
            response.Message = $"Retrieved {pagedObjects.Count} objects (Total: {totalCount})";
        }
        catch (Exception ex)
        {
            response.Success = false;
            response.Message = "Error retrieving objects";
            response.Errors.Add(ex.Message);
        }
        return response;
    }

    public ServiceResponse<List<Object>> GetObjectsByLevel(int levelId)
    {
        var response = new ServiceResponse<List<Object>>();
        try
        {
            var objects = _objects.Where(o => o.LevelId == levelId && !o.IsDeleted).ToList();
            response.Data = objects;
            response.Message = $"Retrieved {objects.Count} objects for Level {levelId}";
        }
        catch (Exception ex)
        {
            response.Success = false;
            response.Message = "Error retrieving objects for level";
            response.Errors.Add(ex.Message);
        }
        return response;
    }

    public ServiceResponse<bool> DeleteObject(int id, bool hardDelete = false)
    {
        var response = new ServiceResponse<bool>();
        try
        {
            var obj = _objects.FirstOrDefault(o => o.Id == id);
            if (obj == null)
            {
                response.Success = false;
                response.Message = "Object not found";
                return response;
            }

            if (hardDelete)
                _objects.Remove(obj);
            else
                obj.IsDeleted = true;
            SaveToDisk();
            response.Data = true;
            response.Message = "Object deleted successfully";
        }
        catch (Exception ex)
        {
            response.Success = false;
            response.Message = "Error deleting object";
            response.Errors.Add(ex.Message);
        }
        return response;
    }
}